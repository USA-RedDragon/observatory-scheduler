using FluentAssertions;
using NINA.Plugin.TargetScheduler.Remote.Api;
using NINA.Plugin.TargetScheduler.Remote.Commands;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    [TestFixture]
    public class PreviewOverridesTest {

        private static PreviewOverride Weight(int id, string rule, double value) {
            return new PreviewOverride { Entity = PreviewOverrides.RuleWeight, Id = id, Field = rule, Value = value };
        }

        private class Row {
            public string Name;
            public double Weight;
        }

        [Test]
        public void EveryProjectWeightsApplyToAnyProject() {
            var o = new List<PreviewOverride> { Weight(0, "Novelty", 40), Weight(0, "Rarity", 25) };
            PreviewOverrides.RuleWeights(o, 7).Should().BeEquivalentTo(new Dictionary<string, double> { ["Novelty"] = 40, ["Rarity"] = 25 });
        }

        [Test]
        public void ProjectWeightWinsOverEveryProjectWeight() {
            var o = new List<PreviewOverride> { Weight(7, "Novelty", 5), Weight(0, "Novelty", 40), Weight(9, "Rarity", 1) };
            PreviewOverrides.RuleWeights(o, 7).Should().BeEquivalentTo(new Dictionary<string, double> { ["Novelty"] = 5 });
            PreviewOverrides.RuleWeights(o, 8).Should().BeEquivalentTo(new Dictionary<string, double> { ["Novelty"] = 40 });
        }

        [Test]
        public void ProjectOverridesAndInvalidWeightsAreIgnored() {
            var o = new List<PreviewOverride> {
                new PreviewOverride { Entity = PreviewOverrides.Project, Id = 7, Field = "priority", Value = 2 },
                Weight(0, "Novelty", 101),
                Weight(0, "Rarity", -1),
                Weight(0, "", 10),
                Weight(0, "Setting Soonest", double.NaN),
                null,
            };
            PreviewOverrides.RuleWeights(o, 7).Should().BeEmpty();
            PreviewOverrides.RuleWeights(null, 7).Should().BeEmpty();
        }

        [Test]
        public void ApplyUpdatesExistingRowsAndAddsMissingOnes() {
            var rows = new List<Row> { new Row { Name = "Novelty", Weight = 10 }, new Row { Name = "Rarity", Weight = 20 } };
            PreviewOverrides.Apply(rows, new Dictionary<string, double> { ["Novelty"] = 40, ["Panel Deficit"] = 30 },
                r => r.Name, (r, w) => r.Weight = w, (n, w) => new Row { Name = n, Weight = w });
            rows.Select(r => (r.Name, r.Weight)).Should().Equal(("Novelty", 40d), ("Rarity", 20d), ("Panel Deficit", 30d));
        }

        [Test]
        public void PreviewReportsTheOverrideKindsItApplies() {
            var json = JsonSerializer.Serialize(new Preview(), CommandJson.Options);
            json.Should().Contain("\"override_kinds\":[\"project\",\"ruleweight\"]");
        }

        [Test]
        public void RuleWeightOverridesDeserialize() {
            var req = JsonSerializer.Deserialize<PreviewRequest>("{\"overrides\":[{\"entity\":\"ruleweight\",\"id\":0,\"field\":\"Novelty\",\"value\":35.5}]}", CommandJson.Options);
            PreviewOverrides.RuleWeights(req.Overrides, 3).Should().BeEquivalentTo(new Dictionary<string, double> { ["Novelty"] = 35.5 });
        }
    }
}
