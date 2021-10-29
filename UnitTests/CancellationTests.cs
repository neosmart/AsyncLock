using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NeoSmart.AsyncLock;

namespace AsyncLockTests
{
    [TestClass]
    public class CancellationTests
    {
        [TestMethod]
        public void CancellingWait()
        {
            var @lock = new AsyncLock();
            var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
            Task.Run(async () =>
            {
                await @lock.LockAsync(cts.Token);
            }).Wait();
            Assert.ThrowsExceptionAsync<OperationCanceledException>(async () =>
            {
                using (await @lock.LockAsync(cts.Token))
                    Assert.Fail("should never reach here if cancellation works properly");
            }).Wait();

        }
        [TestMethod]
        public void CancellingWaitSync()
        {
            var asyncLock = new AsyncLock();
            var cts = new CancellationTokenSource(500);
            Task.Run(async () =>
            {
                using (asyncLock.Lock(cts.Token))
                {
                    // hold the lock until our later attempt is called
                    await Task.Delay(1000);
                }
            });
            Assert.ThrowsException<OperationCanceledException>(() =>
            {
                using (asyncLock.Lock(cts.Token))
                {
                    Assert.Fail("should never reach here if cancellation works properly.");
                }
            });
            // we should still be able to obtain a lock afterward to make sure resources were reobtained
            var newCts= new CancellationTokenSource(1000);
            using (asyncLock.Lock(newCts.Token))
            {
                // reaching this line means the test passed
                // a OperationCanceledException will indicate test failure
            }
        }
    }
}
