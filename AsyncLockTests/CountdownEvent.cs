using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncLockTests
{
    class CountdownEvent : EventWaitHandle
    {
        private int _counter;

        public CountdownEvent(int start)
            : base(false, EventResetMode.ManualReset)
        {
            _counter = start;
        }

        public void Tick()
        {
            var result = Interlocked.Decrement(ref _counter);
            if (result == 0)
            {
                Set();
            }
            else if (result < 0)
            {
                throw new InvalidOperationException("CountdownEvent has been decremented more times than allowed!");
            }
        }
    }
}
