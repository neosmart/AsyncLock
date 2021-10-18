#if TRY_LOCK_OUT_BOOL
using System;
using System.Collections.Generic;
using System.Text;

namespace NeoSmart.AsyncLock
{
    sealed class NullDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
#endif
