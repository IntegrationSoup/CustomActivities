using System;

namespace HL7Soup.Integrations
{
    // A failure that a library explains on purpose, in a sentence it wrote itself.
    // BridgeProvider passes only this sentence to the Host, cleaned and capped; every
    // other exception stays masked, because its text may come from someone else's code
    // and may hold credentials. There is deliberately no constructor that takes an inner
    // exception: a cause belongs in the sentence only if the library writes it out.
    // Build the sentence from what the person configured, never from another exception's
    // Message, and never put a password, key, token, connection string or query string in it.
    internal sealed class ExtensionReasonException : Exception
    {
        internal ExtensionReasonException(string reason) : base(reason) { }
    }
}
