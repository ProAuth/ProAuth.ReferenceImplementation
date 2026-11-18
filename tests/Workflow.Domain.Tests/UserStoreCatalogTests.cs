using Xunit;

namespace Workflow.Domain.Tests;

public class UserStoreCatalogTests
{
    [Theory]
    [InlineData("Server=example;Database=mervikon_partners", true)]
    [InlineData("server=example;Password='a;b;Database=x';Initial Catalog=\"mervikon_partners\"", true)]
    [InlineData("Database=mervikon_partners;Database=another", false)]
    [InlineData("Initial Catalog=another;Database=mervikon_partners", false)]
    [InlineData("Database=mervikon_partners;database=mervikon_partners", false)]
    [InlineData("Server=example;Password='Database=mervikon_partners'", false)]
    [InlineData("Database=\"unterminated", false)]
    [InlineData("ConnectionStringAlias=workshop; Database = mervikon_partners;", true)]
    [InlineData("ConnectionStringAlias=workshop", false)]
    [InlineData("ConnectionStringAlias=workshop;Initial Catalog=mervikon_partners", false)]
    [InlineData("ConnectionStringAlias=workshop;Database=mervikon_partners;Database=other", false)]
    [InlineData("ConnectionStringAlias=workshop;Database='mervikon_partners'", false)]
    [InlineData("ConnectionStringAlias=;Database=mervikon_partners", false)]
    [InlineData("Server=example;Database=mervikon_partners;AttachDbFilename=other.mdf", false)]
    public void RequiresOneUnambiguousEffectiveDatabase(string connection, bool accepted)
    {
        Assert.Equal(accepted, UserStoreCatalog.Matches(connection, "mervikon_partners"));
    }
}
