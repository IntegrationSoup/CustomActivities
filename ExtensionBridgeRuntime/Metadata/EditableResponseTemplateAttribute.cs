using System;

namespace HL7Soup.Integrations
{
    // Runner-only authoring metadata. Legacy version-four activity DLLs do not
    // reference this attribute or depend on the newer designer capability.
    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public sealed class EditableResponseTemplateAttribute : Attribute { }
}