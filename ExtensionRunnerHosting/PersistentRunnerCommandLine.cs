using System.Globalization;
using System.Text;

namespace Popokey.ExtensionRunners
{
    internal static class PersistentRunnerCommandLine
    {
        internal static string BuildServerArguments(string pipeName, int parentProcessId)
        {
            return "--server --pipe-name "
                + QuoteArgument(pipeName)
                + " --parent-pid "
                + parentProcessId.ToString(CultureInfo.InvariantCulture);
        }

        internal static string QuoteArgument(string argument)
        {
            if (string.IsNullOrEmpty(argument))
            {
                return "\"\"";
            }

            bool requiresQuoting = false;
            foreach (char character in argument)
            {
                if (char.IsWhiteSpace(character) || character == '"')
                {
                    requiresQuoting = true;
                    break;
                }
            }

            if (!requiresQuoting)
            {
                return argument;
            }

            StringBuilder builder = new StringBuilder(argument.Length + 2);
            builder.Append('"');

            int backslashCount = 0;
            foreach (char character in argument)
            {
                if (character == '\\')
                {
                    backslashCount++;
                    continue;
                }

                if (character == '"')
                {
                    builder.Append('\\', backslashCount * 2 + 1);
                    builder.Append(character);
                    backslashCount = 0;
                    continue;
                }

                if (backslashCount > 0)
                {
                    builder.Append('\\', backslashCount);
                    backslashCount = 0;
                }

                builder.Append(character);
            }

            if (backslashCount > 0)
            {
                builder.Append('\\', backslashCount * 2);
            }

            builder.Append('"');
            return builder.ToString();
        }
    }
}
