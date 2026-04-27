using FluentMigrator;

namespace BrickBot.Modules.Database.Migrations;

/// <summary>
/// TrainingSamples: per-detection labeled images that produced (or were used to refine) the
/// detection's config. Storing them lets the user re-train after tweaking labels and provides
/// a regression set to verify proposed config changes.
///
/// Image bytes live on disk at data/profiles/{id}/training/{Id}.png; this row carries the
/// label value (e.g. "0.5" for a 50% bar fill) and a free-form note.
/// </summary>
[Migration(202604280003)]
public sealed class _202604280003_CreateTrainingSamplesTable : Migration
{
    public override void Up()
    {
        Create.Table("TrainingSamples")
            .WithColumn("Id").AsString().NotNullable().PrimaryKey()
            .WithColumn("DetectionId").AsString().NotNullable()
            .WithColumn("Label").AsString().Nullable()
            .WithColumn("Note").AsString().Nullable()
            .WithColumn("Width").AsInt32().WithDefaultValue(0)
            .WithColumn("Height").AsInt32().WithDefaultValue(0)
            .WithColumn("CapturedAt").AsString().NotNullable();

        Create.Index("idx_training_detection").OnTable("TrainingSamples").OnColumn("DetectionId");
    }

    public override void Down()
    {
        Delete.Index("idx_training_detection").OnTable("TrainingSamples");
        Delete.Table("TrainingSamples");
    }
}
