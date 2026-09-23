using System;

namespace HL7Soup.Integrations
{
    /// <summary>
    /// Indicates that a property, or an inherited member (when applied to a class), 
    /// should be ignored during AI schema generation.
    /// When used on a class, the provided name specifies an inherited member to ignore.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Class, AllowMultiple = true)]
    public class AiIgnoreAttribute : Attribute
    {
        /// <summary>
        /// Gets the name of the member to ignore (used when the attribute is applied to a class).
        /// </summary>
        public string Name { get; private set; }

        public AiIgnoreAttribute() { }

        public AiIgnoreAttribute(string name)
        {
            Name = name;
        }
    }


    /// <summary>
    /// Provides a description for a property specifically for AI schema generation.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Class, AllowMultiple = false)]
    public class AiDescriptionAttribute : Attribute
    {
        /// <summary>
        /// Gets the description for the AI.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="AiDescriptionAttribute"/> class.
        /// </summary>
        /// <param name="description">The description to use for AI schema generation.</param>
        public AiDescriptionAttribute(string description)
        {
            Description = description;
        }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public sealed class AiPropertyDescriptionAttribute : Attribute
    {
        public string Name { get; }
        public string Description { get; }

        public AiPropertyDescriptionAttribute(string name, string description)
        {
            Name = name;
            Description = description;
        }
    }

    /// <summary>
    /// Provides example values for a property to help the AI understand expected format.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
    public class AiExampleAttribute : Attribute
    {
        /// <summary>
        /// Gets the example value for the AI.
        /// </summary>
        public string Example { get; }

        /// <summary>
        /// Optional description of what the example represents.
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="AiExampleAttribute"/> class.
        /// </summary>
        /// <param name="example">An example value to help the AI understand expected format.</param>
        public AiExampleAttribute(string example)
        {
            Example = example;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AiExampleAttribute"/> class.
        /// </summary>
        /// <param name="example">An example value to help the AI understand expected format.</param>
        /// <param name="description">Optional description of what the example represents.</param>
        public AiExampleAttribute(string example, string description)
        {
            Example = example;
            Description = description;
        }
    }

    /// <summary>
    /// Indicates that a property is required for AI schema generation, even if it's nullable.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class AiRequiredAttribute : Attribute
    {
    }

    /// <summary>
    /// Provides format guidance for AI schema generation.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class AiFormatAttribute : Attribute
    {
        /// <summary>
        /// Gets the format hint for the AI.
        /// </summary>
        public string Format { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="AiFormatAttribute"/> class.
        /// </summary>
        /// <param name="format">The format hint (e.g., "date-time", "email", "uuid", "url").</param>
        public AiFormatAttribute(string format)
        {
            Format = format;
        }
    }

    /// <summary>
    /// Specifies value range constraints for AI schema generation.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class AiRangeAttribute : Attribute
    {
        /// <summary>
        /// Gets the minimum value.
        /// </summary>
        public object Minimum { get; }

        /// <summary>
        /// Gets the maximum value.
        /// </summary>
        public object Maximum { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="AiRangeAttribute"/> class.
        /// </summary>
        /// <param name="minimum">The minimum allowed value.</param>
        /// <param name="maximum">The maximum allowed value.</param>
        public AiRangeAttribute(object minimum, object maximum)
        {
            Minimum = minimum;
            Maximum = maximum;
        }
    }

    /// <summary>
    /// Provides ordering hints for AI schema generation.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
    public class AiOrderAttribute : Attribute
    {
        /// <summary>
        /// Gets the order value for the property.
        /// </summary>
        public int Order { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="AiOrderAttribute"/> class.
        /// </summary>
        /// <param name="order">The order value (lower values come first).</param>
        public AiOrderAttribute(int order)
        {
            Order = order;
        }
    }
}
