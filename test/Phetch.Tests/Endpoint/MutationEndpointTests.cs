namespace Phetch.Tests.Endpoint
{
    using System.Threading.Tasks;
    using FluentAssertions;
    using Phetch.Core;
    using Xunit;

    public class MutationEndpointTests
    {
        [Fact]
        public async Task Returnless_mutation_should_work()
        {
            var mutationArg = 0;
            var endpoint = new MutationEndpoint<int>(
                async val =>
                {
                    mutationArg = val;
                    await Task.Yield();
                }
            );
            var mut = endpoint.Use();

            mut.IsUninitialized.Should().BeTrue();
            mut.Status.Should().Be(QueryStatus.Idle);

            var result = await mut.TriggerAsync(10);

            mut.Status.Should().Be(QueryStatus.Success);
            mutationArg.Should().Be(10);
        }
    }
}
