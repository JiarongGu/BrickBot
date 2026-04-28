using FluentMigrator;

namespace BrickBot.Modules.Database.Migrations;

/// <summary>
/// Add per-sample object-box annotation + tracker init-frame flag to TrainingSamples.
/// Backstory: the v2 training UX lets the user mark WHERE the object lives in each sample
/// (not just one global ROI). Pattern positives can have boxes at different positions; bar
/// samples mark the bar bbox at each fill level; tracker designates exactly one init sample.
/// </summary>
[Migration(202604300001)]
public sealed class _202604300001_AddTrainingSampleObjectBox : Migration
{
    public override void Up()
    {
        Alter.Table("TrainingSamples")
            .AddColumn("ObjectBoxJson").AsString().Nullable()
            .AddColumn("IsInit").AsInt32().NotNullable().WithDefaultValue(0);
    }

    public override void Down()
    {
        Delete.Column("IsInit").FromTable("TrainingSamples");
        Delete.Column("ObjectBoxJson").FromTable("TrainingSamples");
    }
}
