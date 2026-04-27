using FluentMigrator;

namespace BrickBot.Modules.Database.Migrations;

/// <summary>
/// Templates: per-profile vision-template registry. Image bytes live on disk at
/// data/profiles/{id}/templates/{Id}.png; this row carries the user-facing name +
/// description so the filename is no longer a constraint on what users type.
/// </summary>
[Migration(202604280001)]
public sealed class _202604280001_CreateTemplatesTable : Migration
{
    public override void Up()
    {
        Create.Table("Templates")
            .WithColumn("Id").AsString().NotNullable().PrimaryKey()
            .WithColumn("Name").AsString().NotNullable()
            .WithColumn("Description").AsString().Nullable()
            .WithColumn("Width").AsInt32().WithDefaultValue(0)
            .WithColumn("Height").AsInt32().WithDefaultValue(0)
            .WithColumn("CreatedAt").AsString().NotNullable()
            .WithColumn("UpdatedAt").AsString().NotNullable();

        Create.Index("idx_templates_name").OnTable("Templates").OnColumn("Name");
    }

    public override void Down()
    {
        Delete.Index("idx_templates_name").OnTable("Templates");
        Delete.Table("Templates");
    }
}
