using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncLockTests
{
    /// <summary>
    /// A fake resource that will invoke a callback if more than n instances are simultaneously accessed
    /// </summary>
    class LimitedResource
    {
        private readonly int _max = 1;
        private int _unsafe = 0;
        private readonly Action _failureCallback;

        public LimitedResource(Action onFailure, int maxSimultaneous = 1)
        {
            _max = maxSimultaneous;
            _failureCallback = onFailure;
        }

        public void BeginSomethingDangerous()
        {
            if (Interlocked.Increment(ref _unsafe) > _max)
            {
                _failureCallback();
            }
        }

        public void EndSomethingDangerous()
        {
            Interlocked.Decrement(ref _unsafe);
        }
    }
}
