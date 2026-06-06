using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Sweeprr.API.Models;
using Sweeprr.API.Services.Rules;

namespace Sweeprr.Tests.Sweep;

public class LargeLibraryPerformanceTests
{
    [Fact]
    public async Task EvaluateAsync_WithTenThousandItems_CompletesWithinBudget()
    {
        var valueResolver = new ValueResolver();
        var evaluator = new RuleEvaluator(valueResolver);

        // Seed rule group with realistic conditions (multiple sections with logical operators)
        var group = new RuleGroup
        {
            MediaType = MediaType.Movie,
            Rules =
            [
                new Rule { Id = 1, Section = 0, Field = RuleField.FileSizeGb, Comparator = RuleComparator.GreaterThan, Value = "5", ValueType = RuleValueType.Number },
                new Rule { Id = 2, Section = 0, LogicalOperator = LogicalOperator.And, Field = RuleField.PlayCount, Comparator = RuleComparator.GreaterThan, Value = "1", ValueType = RuleValueType.Number },
                new Rule { Id = 3, Section = 1, LogicalOperator = LogicalOperator.Or, Field = RuleField.WatchedByAllUsers, Comparator = RuleComparator.Equals, Value = "true", ValueType = RuleValueType.Bool }
            ]
        };

        // Enumerate 10,000 synthetic items
        var items = new List<MediaContext>(10000);
        for (int i = 0; i < 10000; i++)
        {
            items.Add(new MediaContext
            {
                ItemId = $"jf-{i}",
                Title = $"Movie {i}",
                MediaType = MediaType.Movie,
                FileSizeGb = i % 2 == 0 ? 10m : 2m,
                PlayCount = i % 3,
                WatchedByAllUsers = i % 4 == 0,
                HasTransientFailure = false
            });
        }

        // Warm up
        await evaluator.EvaluateAsync(group, items.Take(100).ToList());

        var sw = Stopwatch.StartNew();
        var results = await evaluator.EvaluateAsync(group, items);
        sw.Stop();

        Assert.Equal(10000, results.Count);
        // Performance budget check: 10,000 items should evaluate in less than 500ms
        Assert.True(sw.ElapsedMilliseconds < 500, $"Evaluation took too long: {sw.ElapsedMilliseconds}ms");
    }
}
