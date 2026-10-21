using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Diagrid.Aspire.Hosting.Catalyst;
using Diagrid.Aspire.Hosting.Catalyst.ComponentSpec;

namespace Hosting.Catalyst.Tests;

public class ManagedServiceTests
{
    private static CatalystProject Build(Func<IResourceBuilder<CatalystProject>, IResourceBuilder<CatalystProject>> configure)
    {
        var builder = DistributedApplication.CreateBuilder();
        return configure(builder.AddCatalystProject("demo")).Resource;
    }

    // Mutation: put the default back to "kvstore" and a Dapr OSS app's `statestore` no longer resolves on Catalyst.
    [Fact]
    public void Default_kv_store_is_named_statestore()
        => Assert.Equal("statestore", Diagrid.Aspire.Hosting.Catalyst.ResourceBuilderExtensions.SampleKvName);

    [Fact]
    public void Default_pubsub_is_unchanged_and_differs_from_the_kv_name()
    {
        Assert.Equal("pubsub", Diagrid.Aspire.Hosting.Catalyst.ResourceBuilderExtensions.SamplePubsubName);
        Assert.NotEqual(Diagrid.Aspire.Hosting.Catalyst.ResourceBuilderExtensions.SampleKvName, Diagrid.Aspire.Hosting.Catalyst.ResourceBuilderExtensions.SamplePubsubName);
    }

    // Mutation: re-add `.WithComponent(...)` to the convenience overload (kv create makes the component itself,
    // so a second `component create` conflicts).
    [Fact]
    public void Kv_convenience_overload_registers_the_store_but_no_duplicate_component()
    {
        var p = Build(b => b.WithCatalystKvStore());

        Assert.True(p.KvStores.ContainsKey("statestore"));
        Assert.Empty(p.PubSubs);
        Assert.Empty(p.Components);
    }

    [Fact]
    public void Pubsub_convenience_overload_registers_the_broker_but_no_duplicate_component()
    {
        var p = Build(b => b.WithCatalystPubSub());

        Assert.True(p.PubSubs.ContainsKey("pubsub"));
        Assert.Empty(p.KvStores);
        Assert.Empty(p.Components);
    }

    // Mutation: generate a component with ActorStateStore = true again. Paired with positive assertions so an
    // empty component set cannot make this pass vacuously.
    [Fact]
    public void Convenience_overloads_do_not_claim_the_actor_state_store_role()
    {
        var p = Build(b => b.WithCatalystKvStore().WithCatalystPubSub());

        Assert.True(p.KvStores.ContainsKey("statestore"));
        Assert.True(p.PubSubs.ContainsKey("pubsub"));
        Assert.DoesNotContain(p.Components.Values, c => c.Metadata.ContainsKey("actorStateStore"));
    }

    [Fact]
    public void Actor_state_store_is_still_available_explicitly()
    {
        var p = Build(b => b.WithCatalystKvStore("statestore")
            .WithComponent("statestore", new DiagridStateStore
            {
                Metadata = new() { State = "statestore", ActorStateStore = true },
            }));

        var flag = Assert.IsType<JsonElement>(p.Components["statestore"].Metadata["actorStateStore"]);
        Assert.True(flag.GetBoolean());
    }
}
