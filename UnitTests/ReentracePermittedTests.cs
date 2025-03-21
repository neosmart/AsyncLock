using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NeoSmart.AsyncLock;

namespace AsyncLockTests;

[TestClass]
public class ReentracePermittedTests
{
    private readonly AsyncLock _lock = new();

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

    // Issue #18
    [TestMethod]
    //[Timeout(5)]
    public async Task BackToBackReentrance()
    {
        var asyncLock = new AsyncLock();
        async Task InnerFunctionAsync()
        {
            using (await asyncLock.LockAsync())
            {
                //
            }
        }
        using (await asyncLock.LockAsync())
        {
            await InnerFunctionAsync();
            await InnerFunctionAsync();
        }
    }
}
