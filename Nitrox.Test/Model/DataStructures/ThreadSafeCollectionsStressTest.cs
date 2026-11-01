using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Nitrox.Model.DataStructures;

/// <summary>
/// The reader/writer tests of the thread-safe collections under contention: the writer of the set test and a list writer run at the same time (this is how the whole suite runs them),
/// and everything is joined before it is looked at. The set test used to return when the writer was done without waiting for the reader's last sample.
/// </summary>
[TestClass]
public class ThreadSafeCollectionsStressTest
{
    [TestMethod]
    public void ASetReadAndWrittenWhileAListIsWrittenKeepsEveryCount()
    {
        for (int round = 0; round < 8; round++)
        {
            Thread neighbour = new(() =>
            {
                ThreadSafeList<int> list = new(200000);
                Random random = new();
                for (int i = 0; i < 200000; i++) { list.Add(random.Next()); }
            });
            neighbour.Start();

            const int iterations = 200000;
            ThreadSafeSet<int> set = new();
            List<long> countsRead = [];
            long addCount = 0;
            ManualResetEventSlim writerDone = new(false);
            Thread reader = new(() =>
            {
                while (!writerDone.IsSet)
                {
                    countsRead.Add(Interlocked.Read(ref addCount));
                    Thread.Yield();
                }

                countsRead.Add(Interlocked.Read(ref addCount));
            });
            Thread writer = new(() =>
            {
                for (int i = 0; i < iterations; i++)
                {
                    set.Add(i);
                    Interlocked.Increment(ref addCount);
                }

                writerDone.Set();
            });
            reader.Start();
            writer.Start();
            writerDone.Wait();
            writer.Join();
            reader.Join();
            neighbour.Join();

            addCount.Should().Be(iterations);
            countsRead.Last().Should().Be(iterations);
            set.Count.Should().Be(iterations);
        }
    }
}
