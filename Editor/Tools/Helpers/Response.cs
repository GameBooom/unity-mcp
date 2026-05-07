// Copyright (C) Funplay. Licensed under MIT.

namespace Funplay.Editor.Tools.Helpers
{
    /// <summary>
    /// Standardized response wrapper for MCP tool returns. Tools that return
    /// <see cref="Response"/> objects (or any non-string object) get serialized
    /// to JSON by <c>FunctionInvokerController</c> so MCP clients can reliably
    /// parse <c>{ success, message, data }</c> instead of free-form strings.
    ///
    /// Success: { success: true, message: "...", data?: {...}, _meta?: {...} }
    /// Error:   { success: false, code: "...", error: "...", data?: {...} }
    /// </summary>
    internal static class Response
    {
        public static object Success(string message, object data = null, object meta = null)
        {
            if (data != null && meta != null)
                return new { success = true, message, data, _meta = meta };
            if (data != null)
                return new { success = true, message, data };
            if (meta != null)
                return new { success = true, message, _meta = meta };
            return new { success = true, message };
        }

        // Use for machine-parsable error codes (UPPERCASE_SNAKE_CASE) plus optional details.
        // The same string is echoed in both `code` and `error` fields so old clients still see a message.
        public static object Error(string errorCodeOrMessage, object data = null)
        {
            if (data != null)
                return new { success = false, code = errorCodeOrMessage, error = errorCodeOrMessage, data };
            return new { success = false, code = errorCodeOrMessage, error = errorCodeOrMessage };
        }
    }
}
