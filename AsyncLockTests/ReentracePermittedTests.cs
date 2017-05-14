using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NeoSmart.AsyncLock;

namespace AsyncLockTests
{
    [TestClass]
    public class ReentracePermittedTests
    {
        readonly AsyncLock _lock = new AsyncLock();

        [TestMethod]
        public void NestedCallReentrance()
        {
            using (_lock.Lock())
            using (_lock.Lock())
            {
                Debug.WriteLine("Hello from NestedCallReentrance!");
            }
        }

        [TestMethod]
        public void NestedAsyncCallReentrance()
        {
            var task = Task.Run(async () =>
            {
                using (await _lock.LockAsync())
                using (await _lock.LockAsync())
                {
                    Debug.WriteLine("Hello from NestedCallReentrance!");
                }
            });

            new TaskWaiter(task).WaitOne();
        }

        private void NestedFunction()
        {
            using (_lock.Lock())
            {
                Debug.WriteLine("Hello from another (nested) function!");
            }
        }

        [TestMethod]
        public void NestedFunctionCallReentrance()
        {
            using (_lock.Lock())
            {
                NestedFunction();
            }
        }
    }
}
