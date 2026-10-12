using FluentAssertions;
using NINA.Plugin.TargetScheduler.Goals;
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace NINA.Plugin.TargetScheduler.Remote.Test {

    [TestFixture]
    public class TemplateRoleStoreTest {
        private TestDb db;

        [SetUp]
        public void SetUp() {
            db = new TestDb();
        }

        [TearDown]
        public void TearDown() {
            db.Dispose();
        }

        private List<string> Columns(string table) {
            var columns = new List<string>();
            using var conn = db.Connect();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table})";
            using var r = cmd.ExecuteReader();
            while (r.Read()) { columns.Add(r.GetString(1)); }
            return columns;
        }

        [Test]
        public void EnsureCreatesBothTablesOnce() {
            using (var conn = db.Connect()) {
                TemplateRoleStore.Ensure(conn);
                TemplateRoleStore.Ensure(conn);
            }
            Columns("ts_template_role").Should().Equal("template_id", "template_guid", "profile_id", "filter", "role", "changed_at");
            Columns("ts_template_migration").Should().Equal("plan_id", "plan_guid", "target_id", "profile_id", "filter", "old_template_id", "old_template_guid",
                "old_template_name", "new_template_id", "migrated_at", "reverted_at");
        }

        [Test]
        public void MissingTableMeansNothingIsManaged() {
            using var conn = db.Connect();
            var table = TemplateRoleStore.Load(conn);
            table.Should().BeSameAs(ManagedTemplateTable.Empty);
            table.IsManaged(1).Should().BeFalse();
        }

        [Test]
        public void ReadsRowsAsSymmetricDsWritesThem() {
            using (var conn = db.Connect()) { TemplateRoleStore.Ensure(conn); }
            db.Exec(@"INSERT INTO ts_template_role (template_id, template_guid, profile_id, filter, role, changed_at) VALUES
('40', 'a', 'P', 'Red', 'managed', '2026-10-12T18:00:00Z'),
(10, 'b', 'P', 'Red', 'retired', '2026-10-12 18:00:00.000'),
(11, NULL, 'P', 'Blue', 'Retired', '2026-10-12T18:00:00Z')");
            ManagedTemplateTable table;
            using (var conn = db.Connect()) { table = TemplateRoleStore.Load(conn); }
            table.Count.Should().Be(3);
            table.ManagedCount.Should().Be(1);
            table.IsManaged(40).Should().BeTrue();
            table.IsRetired(10).Should().BeTrue();
            table.IsRetired(11).Should().BeTrue();
            table.IsManaged(10).Should().BeFalse();
            table.HasManaged("P").Should().BeTrue();
        }
    }
}
