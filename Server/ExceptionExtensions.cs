//------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
//------------------------------------------------------------

using System.Runtime.InteropServices;

namespace SdkLspServer;

/// <summary>
/// Extension methods for exception handling.
/// </summary>
internal static class ExceptionExtensions
{
    /// <summary>
    /// Determines whether the exception is fatal and should not be caught.
    /// </summary>
    /// <param name="exception">The exception to check.</param>
    public static bool IsFatal(this Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception is OutOfMemoryException ||
               exception is StackOverflowException ||
               exception is AccessViolationException ||
               exception is SEHException ||
               exception is ThreadAbortException;
    }
}
