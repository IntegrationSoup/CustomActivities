using HL7Soup.Integrations;
using System;
using System.Reflection;

namespace ValidateTransformer
{
    /// <summary>
    /// Bridges two public v4 WorkflowInstance methods that were not exposed on
    /// IWorkflowInstance. Failures are deliberately swallowed so an unavailable
    /// bridge can never recreate the no-ACK exception path.
    /// </summary>
    internal static class WorkflowErrorBridge
    {
        internal static bool TryPromoteResponse(IWorkflowInstance workflowInstance, IMessage responseMessage)
        {
            if (workflowInstance == null || responseMessage == null)
            {
                return false;
            }

            return TryInvoke(workflowInstance, "SetReponseMessage", responseMessage);
        }

        internal static bool TryMarkErrored(IWorkflowInstance workflowInstance, string errorMessage)
        {
            if (workflowInstance == null)
            {
                return false;
            }

            bool marked = TryInvoke(workflowInstance, "Errored", errorMessage ?? string.Empty);

            try
            {
                // This uses the supported interface and also records an error event
                // for the current message. If Errored(string) was available, its
                // more useful ErrorMessage value remains intact.
                workflowInstance.SetVariable("WORKFLOWERROR", "true");
                marked = true;
            }
            catch
            {
                // Do not let error bookkeeping suppress the response.
            }

            return marked;
        }

        private static bool TryInvoke(object target, string methodName, object argument)
        {
            try
            {
                MethodInfo method = FindCompatibleMethod(target.GetType(), methodName, argument);
                if (method == null)
                {
                    return false;
                }

                method.Invoke(target, new[] { argument });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static MethodInfo FindCompatibleMethod(Type targetType, string methodName, object argument)
        {
            MethodInfo[] methods = targetType.GetMethods(BindingFlags.Instance | BindingFlags.Public);
            foreach (MethodInfo method in methods)
            {
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 1)
                {
                    continue;
                }

                if (argument == null || parameters[0].ParameterType.IsInstanceOfType(argument))
                {
                    return method;
                }
            }

            return null;
        }
    }
}
