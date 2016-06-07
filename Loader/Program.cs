using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Loader
{
    class Program
    {
        static void Main(string[] args)
        {
            var responseTimeTarget = 250L;
            var periodResults = new List<PeriodResult>();

            var t = Task.Run(async () =>
            {
                var hitsPerSecond = 1;
                var lastSuccessfulHitsPerSecond = 0;

                Func<int, int> getNextHitsPerSecond = Double;
                var exitOnFailure = false;

                while (true)
                {
                    Console.Title = $"Target throughput: {hitsPerSecond} hits/s";

                    var sw = Stopwatch.StartNew();

                    var result = await TestLoad(
                       period: (long)TimeSpan.FromSeconds(10).TotalMilliseconds,
                       hitsPerSecond: hitsPerSecond,
                       script: new []
                       {
                       });

                    sw.Stop();


                    periodResults.Add(result);

                    var averageResponseTime = result.StepResults.Average(r => r.ResponseTime);
                    Console.WriteLine($"Target throughput: {hitsPerSecond} hits/s, Average throughput: {Math.Round(result.StepResults.Length / sw.Elapsed.TotalSeconds, 2)}, averageResponseTime: {(long)averageResponseTime}ms, number of hits: {result.StepResults.Length}");

                    if (responseTimeTarget > averageResponseTime)
                    {
                        lastSuccessfulHitsPerSecond = hitsPerSecond;
                        hitsPerSecond = getNextHitsPerSecond(hitsPerSecond);
                    }
                    else if (exitOnFailure)
                    {
                        break;
                    }
                    else
                    {
                        getNextHitsPerSecond = Incremental;
                        hitsPerSecond = getNextHitsPerSecond(lastSuccessfulHitsPerSecond);
                        exitOnFailure = true;
                    }
                }
            });

            Task.WaitAll(t);
        }

        static int Double(int i) => i * 2;
        static int Incremental(int i) => Math.Max(1 + 1, (int)(i * 1.1));

        static Step MakeGet(string name, string url)
        {
            return new Step(name, async (cli, sw, offset, index) =>
            {
                sw.Restart();
                var response = await cli.GetAsync(url);
                sw.Stop();

                return new StepResult(offset, 0, sw.ElapsedMilliseconds);
            });
        }

        static async Task<PeriodResult> TestLoad(long period, int hitsPerSecond, Step[] script)
        {
            var stepResults = new ConcurrentBag<StepResult>();

            // TODO: Convert hitsPerSecond to concurrency and delay so that the load can be more fine tuned

            var tasks = Enumerable
                .Range(0, hitsPerSecond)
                .Select(i => Task.Run(async () =>
                {
                    var stepSw = new Stopwatch();
                    using (var cli = new HttpClient())
                    {
                        cli.DefaultRequestHeaders.AcceptEncoding.ParseAdd("gzip");

                        for (var sw = Stopwatch.StartNew(); sw.ElapsedMilliseconds < period;)
                        {
                            for (int j = 0; j < script.Length; j++)
                            {
                                var stepResult = await script[j].Func(cli, stepSw, sw.ElapsedMilliseconds, j);
                                stepResults.Add(stepResult);
                            }
                        }
                    }
                }))
                .ToArray();

            await Task.WhenAll(tasks);

            var sortedResults = stepResults
                .OrderBy(r => r.Offset)
                .ToArray();

            return new PeriodResult(period, hitsPerSecond, sortedResults);
        }

        class Step
        {
            public string Name { get; }
            public StepFn Func { get; }

            public Step(string name, StepFn func)
            {
                this.Name = name;
                this.Func = func;
            }
        }

        delegate Task<StepResult> StepFn(HttpClient cli, Stopwatch sw, long offset, int index);

        class StepResult
        {
            public long Offset { get; }
            public int Step { get; }
            public long ResponseTime { get; }

            public StepResult(long offset, int step, long elapsed)
            {
                this.Offset = offset;
                this.Step = step;
                this.ResponseTime = elapsed;
            }
        }

        class PeriodResult
        {
            public long Length { get; }
            public int Concurrency { get; }
            public StepResult[] StepResults { get; }

            public PeriodResult(long length, int concurrency, StepResult[] stepResults)
            {
                this.Length = length;
                this.Concurrency = concurrency;
                this.StepResults = stepResults;
            }
        }
    }
}
