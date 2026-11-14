using Microsoft.EntityFrameworkCore;
using RaceDay.Api.Data;
using Xunit;

namespace RaceDay.Tests;

public class DbContextTests
{
    // the fact below is a bit of a hack, but it works for now. We could also store the allowed
    [Fact]
    public void Model_UsesTheSameSixTablesAsThePart1Erd()
    {
        var options = new DbContextOptionsBuilder<RaceDayDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        using var context = new RaceDayDbContext(options);

        var tables = context.Model.GetEntityTypes()
            .Select(t => t.GetTableName())
            .OrderBy(n => n)
            .ToList();

        Assert.Equal(
            new[] { "Category", "Enrolment", "Event", "Participant", "Result", "User" },
            tables);
    }
}
