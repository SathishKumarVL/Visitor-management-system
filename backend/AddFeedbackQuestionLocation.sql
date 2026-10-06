-- Additive: location scope for feedback questions (idempotent).
IF COL_LENGTH('dbo.FeedbackQuestions', 'LocationId') IS NULL
  ALTER TABLE dbo.FeedbackQuestions ADD LocationId uniqueidentifier NULL;

IF NOT EXISTS (
  SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_FeedbackQuestions_Locations_LocationId')
BEGIN
  ALTER TABLE dbo.FeedbackQuestions WITH CHECK
    ADD CONSTRAINT FK_FeedbackQuestions_Locations_LocationId
    FOREIGN KEY (LocationId) REFERENCES dbo.Locations (Id);
END

IF NOT EXISTS (
  SELECT 1 FROM sys.indexes WHERE name = N'IX_FeedbackQuestions_LocationId' AND object_id = OBJECT_ID(N'dbo.FeedbackQuestions'))
  CREATE INDEX IX_FeedbackQuestions_LocationId ON dbo.FeedbackQuestions (LocationId);

IF NOT EXISTS (
  SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId = N'20260926110000_AddFeedbackQuestionLocation')
  INSERT INTO dbo.__EFMigrationsHistory (MigrationId, ProductVersion)
  VALUES (N'20260926110000_AddFeedbackQuestionLocation', N'9.0.0');
