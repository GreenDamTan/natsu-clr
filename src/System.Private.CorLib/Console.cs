using System;
using System.Runtime.CompilerServices;

namespace System
{
    public static class Console
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        public extern static void SayHello(float value);

        public static void Test()
        {
            SayHello(100);
        }
    }
}
