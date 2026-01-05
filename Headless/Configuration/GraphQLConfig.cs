namespace Headless.Configuration;

public record GraphQLConfig
(
    bool Enabled = true,
    string Path = "/graphql",
    int Port = 5050
)
{
    public GraphQLConfig() : this(true, "/graphql", 5050)
    {
    }
}
