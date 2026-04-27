using FluentMigrator;

namespace BrickBot.Modules.Database.Migrations;

/// <summary>
/// Detections: per-profile vision-rule registry. Definition stored as a JSON blob in
/// <c>DefinitionJson</c> so adding new fields to <c>DetectionDefinition</c> doesn't require a
/// schema migration. Indexes on (Name, Kind, [Group]) drive list / filter queries.
/// </summary>
[Migration(202604280002)]
public sealed class _202604280002_CreateDetectionsTable : Migration
{
    public override void Up()
    {
        Create.Table("Detections")
            .WithColumn("Id").AsString().NotNullable().PrimaryKey()
            .WithColumn("Name").AsString().NotNullable()
            .WithColumn("Kind").AsString().NotNullable()
            .WithColumn("Group").AsString().Nullable()
            .WithColumn("Enabled").AsInt32().WithDefaultValue(1)
            .WithColumn("DefinitionJson").AsString().NotNullable()
            .WithColumn("CreatedAt").AsString().NotNullable()
            .WithColumn("UpdatedAt").AsString().NotNullable();

        Create.Index("idx_detections_kind").OnTable("Detections").OnColumn("Kind");
        Create.Index("idx_detections_group").OnTable("Detections").OnColumn("Group");
    }

    public override void Down()
    {
        Delete.Index("idx_detections_group").OnTable("Detections");
        Delete.Index("idx_detections_kind").OnTable("Detections");
        Delete.Table("Detections");
    }
}
