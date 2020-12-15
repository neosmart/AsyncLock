using System.Diagnostics;
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
        public async Task NestedCallReentrance()
        {
            using (await _lock.LockAsync())
            using (await _lock.LockAsync())
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

        private async Task NestedFunctionAsync()
        {
            using (await _lock.LockAsync())
            {
                Debug.WriteLine("Hello from another (nested) function!");
            }
        }

        [TestMethod]
        public async Task NestedFunctionCallReentrance()
        {
            using (await _lock.LockAsync())
            {
                await NestedFunctionAsync();
            }
        }
    }
}
