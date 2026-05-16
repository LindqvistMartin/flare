using Xunit;

namespace Flare.Tests.Integration.Infrastructure;

[CollectionDefinition("Api")]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
}
