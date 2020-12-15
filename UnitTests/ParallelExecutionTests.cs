using Microsoft.VisualStudio.TestTools.UnitTesting;
using NeoSmart.AsyncLock;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace AsyncLockTests
{
    /// <summary>
    /// Creates multiple indepndent tasks, each with its own lock, and runs them
    /// all in parallel. There should be no contention for the lock between the
    /// parallelly executed tasks, but each task then recursively obtains what
    /// should be the same lock - which should again be contention-free - after
    /// an await point that may or may not resume on the same actual thread the
    /// previous lock was obtained with.
    /// </summary>
    [TestClass]
    public class ParallelExecutionTests
    {
        [TestMethod]
        public async Task ParallelExecution()
        {
            await Task.WhenAll(Enumerable.Range(0, 1).Select(SomeMethod));
        }

        private static async Task SomeMethod(int i)
        {
            var asyncLock = new AsyncLock();
            System.Diagnostics.Debug.WriteLine($"Outside {i}");
            await Task.Delay(100);
            using (await asyncLock.LockAsync())
            {
                System.Diagnostics.Debug.WriteLine($"Lock1 {i}");
                await Task.Delay(100);
                using (await asyncLock.LockAsync())
                {
                    System.Diagnostics.Debug.WriteLine($"Lock2 {i}");
                    await Task.Delay(100);
                }
            }
        }
    }
}
