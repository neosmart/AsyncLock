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
    }
}
