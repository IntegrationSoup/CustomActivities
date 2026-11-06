using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace AzureActivities.Tests
{
    internal abstract class ReadOnlyFixtureStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    internal sealed class InterruptedStream : ReadOnlyFixtureStream
    {
        private readonly byte[] prefix;
        private readonly string error;
        private int position;
        internal InterruptedStream(params byte[] prefix) : this("Synthetic interrupted response body.", prefix) { }
        internal InterruptedStream(string error, params byte[] prefix) { this.prefix = prefix; this.error = error; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (position == prefix.Length) throw new IOException(error);
            int read = Math.Min(count, prefix.Length - position);
            Buffer.BlockCopy(prefix, position, buffer, offset, read);
            position += read;
            return read;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Read(buffer, offset, count));
        }
    }

    internal sealed class WaitingStream : ReadOnlyFixtureStream
    {
        internal readonly TaskCompletionSource<CancellationToken> Started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool Disposed;
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("The download must use cancellable async reads.");
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            Started.TrySetResult(cancellationToken);
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            return 0;
        }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }
}
