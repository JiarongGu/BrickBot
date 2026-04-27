using FluentMigrator;

namespace BrickBot.Modules.Database.Migrations;

/// <summary>
/// Recordings: named multi-frame captures users record once and reuse across many
/// detection trainings. Frames live on disk at
/// <c>data/profiles/{id}/recordings/{recordingId}/frame-{n}.png</c>.
///
/// Decoupling recording from training lets the same gameplay session train an HP-bar
/// detection, an MP-bar detection, and a "boss visible" detection without re-recording.
/// </summary>
[Migration(202604290001)]
public sealed class _202604290001_CreateRecordingsTables : Migration
{
    public override void Up()
    {
        Create.Table("Recordings")
            .WithColumn("Id").AsString().NotNullable().PrimaryKey()
            .WithColumn("Name").AsString().NotNullable()
            .WithColumn("Description").AsString().Nullable()
            .WithColumn("WindowTitle").AsString().Nullable()
            .WithColumn("Width").AsInt32().WithDefaultValue(0)
            .WithColumn("Height").AsInt32().WithDefaultValue(0)
            .WithColumn("FrameCount").AsInt32().WithDefaultValue(0)
            .WithColumn("IntervalMs").AsInt32().WithDefaultValue(0)
            .WithColumn("CreatedAt").AsString().NotNullable()
            .WithColumn("UpdatedAt").AsString().NotNullable();

        Create.Index("idx_recordings_name").OnTable("Recordings").OnColumn("Name");

        Create.Table("RecordingFrames")
            .WithColumn("Id").AsString().NotNullable().PrimaryKey()
            .WithColumn("RecordingId").AsString().NotNullable()
            .WithColumn("FrameIndex").AsInt32().NotNullable()
            .WithColumn("Width").AsInt32().WithDefaultValue(0)
            .WithColumn("Height").AsInt32().WithDefaultValue(0)
            .WithColumn("CapturedAt").AsString().NotNullable();

        Create.Index("idx_recordingframes_recording").OnTable("RecordingFrames").OnColumn("RecordingId");
    }

    public override void Down()
    {
        Delete.Index("idx_recordingframes_recording").OnTable("RecordingFrames");
        Delete.Table("RecordingFrames");
        Delete.Index("idx_recordings_name").OnTable("Recordings");
        Delete.Table("Recordings");
    }
}
