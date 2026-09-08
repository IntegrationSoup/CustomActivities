using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HL7Soup.Integrations.ExtensionBridge;

namespace Popokey.ExtensionRunners
{
    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public sealed class ExtensionDesignerAttribute : Attribute
    {
        public ExtensionDesignerAttribute(Type handlerType) { HandlerType = handlerType; }
        public Type HandlerType { get; }
    }
    // Optional runner capability; not a requirement on the legacy activity API.
    public interface IExtensionDesigner
    {
        ExtensionDesignerCapabilities Describe();
        ExtensionDesignerResult Update(ExtensionDesignerRequest request, CancellationToken cancellation);
    }
    internal sealed partial class BridgeProvider
    {
        private static IExtensionDesigner CreateDesigner(Type type)
        {
            var attribute = type.GetCustomAttributes(typeof(ExtensionDesignerAttribute), true).Cast<ExtensionDesignerAttribute>().SingleOrDefault();
            if (attribute == null) return null;
            if (!typeof(IExtensionDesigner).IsAssignableFrom(attribute.HandlerType)) throw BridgeFault.Invalid("Invalid designer handler declaration.");
            return (IExtensionDesigner)Activator.CreateInstance(attribute.HandlerType);
        }
        private ExtensionDesignerResult UpdateDesigner(string requestId, ExtensionDesignerRequest request)
        {
            ValidateIdentity(request.SchemaVersion, request.ProviderId, false);
            var descriptor = Resolve(request.TypeName);
            if (request.ProviderVersion != catalog.ProviderVersion || request.Kind != descriptor.Kind || descriptor.Designer == null || request.Revision < 0)
                throw BridgeFault.Invalid("Refresh the extension declaration before updating fields.");
            var values = NamedValues(request.Parameters);
            if (values.Count > 256 || values.Keys.Any(k => !descriptor.Parameters.Any(p => p.Name == k))) throw BridgeFault.Invalid("Unknown designer field.");
            bool action = request.EventType == "Action" && descriptor.Designer.Actions.Any(a => a.Id == request.ControlName);
            bool change = request.EventType == "FieldChanged" && descriptor.Parameters.Any(p => p.Name == request.ControlName);
            bool initialize = request.EventType == "Initialize" && string.IsNullOrEmpty(request.ControlName);
            if (!action && (!(change || initialize) || !descriptor.Designer.FieldChanges)) throw BridgeFault.Invalid("Unsupported designer event.");
            DateTimeOffset deadline;
            if (!DateTimeOffset.TryParse(request.DeadlineUtc, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out deadline))
                throw BridgeFault.Invalid("A designer deadline is required.");
            double remaining = (deadline - DateTimeOffset.UtcNow).TotalMilliseconds;
            if (remaining <= 0 || remaining > 30000) throw BridgeFault.Invalid("Designer deadline is expired or too long.");
            if (!Monitor.TryEnter(businessGate, (int)remaining)) throw new BridgeFault("DeadlineExceeded", "NotStarted", "The extension is busy.");
            try
            {
                remaining = (deadline - DateTimeOffset.UtcNow).TotalMilliseconds;
                if (remaining <= 0) throw new BridgeFault("DeadlineExceeded", "NotStarted", "The designer request expired.");
                if (dispatched.Count >= 10000 || !dispatched.Add(requestId)) throw BridgeFault.Invalid("This request cannot be replayed.");
                using (var cancellation = new CancellationTokenSource((int)remaining))
                {
                    var result = CreateDesigner(types[descriptor.TypeName]).Update(request, cancellation.Token) ?? new ExtensionDesignerResult();
                    if (cancellation.IsCancellationRequested || DateTimeOffset.UtcNow >= deadline) throw new BridgeFault("DeadlineExceeded", "Unknown", "The designer result arrived too late.");
                    result.Revision = request.Revision;
                    return result;
                }
            }
            finally { Monitor.Exit(businessGate); }
        }
    }
    public sealed class SftpDesigner : IExtensionDesigner
    {
        public ExtensionDesignerCapabilities Describe() => new ExtensionDesignerCapabilities { FieldChanges = true };
        public ExtensionDesignerResult Update(ExtensionDesignerRequest request, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            string key = request.Parameters.FirstOrDefault(p => p.Name == "Private Key Path")?.Value;
            return new ExtensionDesignerResult { Fields = new List<ExtensionDesignerFieldUpdate> {
                new ExtensionDesignerFieldUpdate { Name = "Private Key Passphrase", IsVisible = !string.IsNullOrWhiteSpace(key) }
            } };
        }
    }
}