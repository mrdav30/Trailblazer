using FluentAssertions;
using System;
using System.Collections.Generic;
using Trailblazer.Serialization;
using Xunit;

namespace Trailblazer.Tests.Serialization;

public class RecordLinkSerializationTests
{
    [Fact]
    public void JsonRoundTrip_ShouldResolveImmediateLinks_UsingResolversAndSlots()
    {
        var sourcePrimary = new ExternalResource("source-primary");
        var sourceSecondary = new ExternalResource("source-secondary");

        var saveContext = new ChronicleContext();
        var savePrimaryResolver = new DictionaryLinkResolver<IExternalResource>();
        var saveSecondaryResolver = new DictionaryLinkResolver<IExternalResource>();
        savePrimaryResolver.Register("primary", sourcePrimary);
        saveSecondaryResolver.Register("secondary", sourceSecondary);
        saveContext.Links.RegisterResolver(savePrimaryResolver);
        saveContext.Links.RegisterResolver(saveSecondaryResolver, slot: "secondary");

        var source = new ResolverLinkRecord()
        {
            Name = "alpha",
            Primary = sourcePrimary,
            Secondary = sourceSecondary
        };

        string json = JsonRecordSerializer.Serialize(source, saveContext, writeIndented: true);

        var loadPrimary = new ExternalResource("load-primary");
        var loadSecondary = new ExternalResource("load-secondary");

        var loadContext = new ChronicleContext();
        var loadPrimaryResolver = new DictionaryLinkResolver<IExternalResource>();
        var loadSecondaryResolver = new DictionaryLinkResolver<IExternalResource>();
        loadPrimaryResolver.Register("primary", loadPrimary);
        loadSecondaryResolver.Register("secondary", loadSecondary);
        loadContext.Links.RegisterResolver(loadPrimaryResolver);
        loadContext.Links.RegisterResolver(loadSecondaryResolver, slot: "secondary");

        var target = new ResolverLinkRecord();
        JsonRecordSerializer.Populate(target, json, loadContext);

        target.Name.Should().Be("alpha");
        target.Primary.Should().BeSameAs(loadPrimary);
        target.Secondary.Should().BeSameAs(loadSecondary);
    }

    [Fact]
    public void MemoryPackRoundTrip_ShouldResolveImmediateLinks_UsingResolversAndSlots()
    {
        var sourcePrimary = new ExternalResource("source-primary");
        var sourceSecondary = new ExternalResource("source-secondary");

        var saveContext = new ChronicleContext();
        var savePrimaryResolver = new DictionaryLinkResolver<IExternalResource>();
        var saveSecondaryResolver = new DictionaryLinkResolver<IExternalResource>();
        savePrimaryResolver.Register("primary", sourcePrimary);
        saveSecondaryResolver.Register("secondary", sourceSecondary);
        saveContext.Links.RegisterResolver(savePrimaryResolver);
        saveContext.Links.RegisterResolver(saveSecondaryResolver, slot: "secondary");

        var source = new ResolverLinkRecord()
        {
            Name = "alpha",
            Primary = sourcePrimary,
            Secondary = sourceSecondary
        };

        byte[] data = MemoryPackRecordSerializer.Serialize(source, saveContext);

        var loadPrimary = new ExternalResource("load-primary");
        var loadSecondary = new ExternalResource("load-secondary");

        var loadContext = new ChronicleContext();
        var loadPrimaryResolver = new DictionaryLinkResolver<IExternalResource>();
        var loadSecondaryResolver = new DictionaryLinkResolver<IExternalResource>();
        loadPrimaryResolver.Register("primary", loadPrimary);
        loadSecondaryResolver.Register("secondary", loadSecondary);
        loadContext.Links.RegisterResolver(loadPrimaryResolver);
        loadContext.Links.RegisterResolver(loadSecondaryResolver, slot: "secondary");

        var target = new ResolverLinkRecord();
        MemoryPackRecordSerializer.Populate(target, data, loadContext);

        target.Name.Should().Be("alpha");
        target.Primary.Should().BeSameAs(loadPrimary);
        target.Secondary.Should().BeSameAs(loadSecondary);
    }

    [Fact]
    public void JsonRoundTrip_ShouldResolveDeferredLinks_AfterGraphLoad()
    {
        var source = CreateDeferredGraph("shared-link");

        var saveContext = new ChronicleContext();
        saveContext.Links.RegisterInstance("shared-link", source.Provider.Resource, slot: "provider");

        string json = JsonRecordSerializer.Serialize(source, saveContext, writeIndented: true);

        var target = new DeferredLinkGraph();
        JsonRecordSerializer.Populate(target, json, new ChronicleContext());

        target.Consumer.Label.Should().Be("consumer");
        target.Provider.Id.Should().Be("shared-link");
        target.Consumer.Resource.Should().BeSameAs(target.Provider.Resource);
        target.Consumer.Resource.Name.Should().Be("loaded-shared-link");
    }

    [Fact]
    public void MemoryPackRoundTrip_ShouldResolveDeferredLinks_AfterGraphLoad()
    {
        var source = CreateDeferredGraph("shared-link");

        var saveContext = new ChronicleContext();
        saveContext.Links.RegisterInstance("shared-link", source.Provider.Resource, slot: "provider");

        byte[] data = MemoryPackRecordSerializer.Serialize(source, saveContext);

        var target = new DeferredLinkGraph();
        MemoryPackRecordSerializer.Populate(target, data, new ChronicleContext());

        target.Consumer.Label.Should().Be("consumer");
        target.Provider.Id.Should().Be("shared-link");
        target.Consumer.Resource.Should().BeSameAs(target.Provider.Resource);
        target.Consumer.Resource.Name.Should().Be("loaded-shared-link");
    }

    private static DeferredLinkGraph CreateDeferredGraph(string id)
    {
        var resource = new ExternalResource("source-resource");
        return new DeferredLinkGraph()
        {
            Consumer = new DeferredLinkConsumer()
            {
                Label = "consumer",
                Resource = resource
            },
            Provider = new DeferredLinkProvider()
            {
                Id = id,
                Resource = resource
            }
        };
    }

    private interface IExternalResource
    {
        string Name { get; }
    }

    private sealed class ExternalResource : IExternalResource
    {
        public ExternalResource(string name)
        {
            Name = name;
        }

        public string Name { get; }
    }

    private sealed class ResolverLinkRecord : IRecordable
    {
        public string Name = string.Empty;

        public IExternalResource? Primary;

        public IExternalResource? Secondary;

        public void RecordData(IChronicler chronicler)
        {
            string name = Name;
            IExternalResource? primary = Primary;
            IExternalResource? secondary = Secondary;

            RecordValues.Look(chronicler, ref name, "name", name);
            RecordLinks.Look(chronicler, ref primary, "primary");
            RecordLinks.Look(chronicler, ref secondary, "secondary", slot: "secondary");

            if (chronicler.Mode == SerializationMode.Loading)
            {
                Name = name;
                Primary = primary;
                Secondary = secondary;
            }
        }
    }

    private sealed class DeferredLinkGraph : IRecordable
    {
        public DeferredLinkConsumer Consumer = new();

        public DeferredLinkProvider Provider = new();

        public void RecordData(IChronicler chronicler)
        {
            DeferredLinkConsumer consumer = Consumer;
            DeferredLinkProvider provider = Provider;

            RecordDeep.Look(chronicler, ref consumer, "consumer");
            RecordDeep.Look(chronicler, ref provider, "provider");

            if (chronicler.Mode == SerializationMode.Loading)
            {
                Consumer = consumer;
                Provider = provider;
            }
        }
    }

    private sealed class DeferredLinkConsumer : IRecordable
    {
        public string Label = string.Empty;

        public ExternalResource? Resource;

        public void RecordData(IChronicler chronicler)
        {
            string label = Label;
            ExternalResource? resource = Resource;

            RecordValues.Look(chronicler, ref label, "label", label);
            RecordLinks.LookDeferred(
                chronicler,
                resource,
                "resource",
                resolved => Resource = resolved,
                slot: "provider");

            if (chronicler.Mode == SerializationMode.Loading)
            {
                Label = label;
            }
        }
    }

    private sealed class DeferredLinkProvider : IRecordable
    {
        public string Id = string.Empty;

        public ExternalResource? Resource;

        public void RecordData(IChronicler chronicler)
        {
            string id = Id;

            RecordValues.Look(chronicler, ref id, "id", id);

            if (chronicler.Mode == SerializationMode.Loading)
            {
                Id = id;
                Resource = new ExternalResource($"loaded-{id}");
                chronicler.Context.Links.RegisterInstance(id, Resource, slot: "provider");
            }
        }
    }

    private sealed class DictionaryLinkResolver<T> : IRecordLinkResolver<T>
    {
        private readonly Dictionary<string, T> _values = new(StringComparer.Ordinal);

        public void Register(string id, T value)
        {
            _values[id] = value;
        }

        public bool TryGetReferenceId(T value, out string id)
        {
            foreach (KeyValuePair<string, T> pair in _values)
            {
                if (!ValuesMatch(pair.Value, value))
                    continue;

                id = pair.Key;
                return true;
            }

            id = string.Empty;
            return false;
        }

        public bool TryResolveReference(string id, out T value)
        {
            if (_values.TryGetValue(id, out T? resolvedValue))
            {
                value = resolvedValue!;
                return true;
            }

            value = default!;
            return false;
        }

        private static bool ValuesMatch(T left, T right)
        {
            if (typeof(T).IsValueType)
                return EqualityComparer<T>.Default.Equals(left, right);

            return ReferenceEquals(left, right);
        }
    }
}
