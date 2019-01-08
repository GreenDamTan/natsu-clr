// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.Serialization;

namespace System
{
    [Serializable]
    public class SystemException : Exception
    {
        public SystemException()
            : base(nameof(SystemException))
        {
            HResult = HResults.COR_E_SYSTEM;
        }

        public SystemException(string message)
            : base(message)
        {
            HResult = HResults.COR_E_SYSTEM;
        }

        public SystemException(string message, Exception innerException)
            : base(message, innerException)
        {
            HResult = HResults.COR_E_SYSTEM;
        }
    }
}
