IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709175152_InitialCreate'
)
BEGIN
    CREATE TABLE [AuditLogs] (
        [Id] uniqueidentifier NOT NULL,
        [TimestampUtc] datetimeoffset NOT NULL,
        [UserId] uniqueidentifier NULL,
        [Username] nvarchar(64) NOT NULL,
        [SourceIp] nvarchar(64) NOT NULL,
        [Action] int NOT NULL,
        [ResourceType] nvarchar(64) NOT NULL,
        [ResourceId] uniqueidentifier NULL,
        [ResourceName] nvarchar(128) NULL,
        [Details] nvarchar(1024) NULL,
        [IsCritical] bit NOT NULL,
        CONSTRAINT [PK_AuditLogs] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709175152_InitialCreate'
)
BEGIN
    CREATE TABLE [Secrets] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(128) NOT NULL,
        [ProjectKey] nvarchar(64) NOT NULL,
        [Environment] int NOT NULL,
        [Sensitivity] int NOT NULL,
        [Ciphertext] varbinary(max) NOT NULL,
        [WrappedDek] varbinary(max) NOT NULL,
        [KekId] nvarchar(64) NOT NULL,
        [Algorithm] nvarchar(32) NOT NULL,
        [IsHoneyToken] bit NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [UpdatedAtUtc] datetimeoffset NULL,
        [ExpiresAtUtc] datetimeoffset NULL,
        [IsShredded] bit NOT NULL,
        [CreatedByUserId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_Secrets] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709175152_InitialCreate'
)
BEGIN
    CREATE TABLE [TrustedNetworks] (
        [Id] uniqueidentifier NOT NULL,
        [Cidr] nvarchar(64) NOT NULL,
        [Label] nvarchar(128) NOT NULL,
        [AddedBy] nvarchar(64) NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        CONSTRAINT [PK_TrustedNetworks] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709175152_InitialCreate'
)
BEGIN
    CREATE TABLE [Users] (
        [Id] uniqueidentifier NOT NULL,
        [Username] nvarchar(64) NOT NULL,
        [DisplayName] nvarchar(64) NOT NULL,
        [Email] nvarchar(256) NOT NULL,
        [PasswordHash] varbinary(64) NOT NULL,
        [PasswordSalt] varbinary(32) NOT NULL,
        [TotpSecret] nvarchar(128) NULL,
        [TotpEnabled] bit NOT NULL,
        [Clearance] int NOT NULL,
        [ProjectKey] nvarchar(64) NOT NULL,
        [IsDisabled] bit NOT NULL,
        [AvatarUpdatedAtUtc] datetimeoffset NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709175152_InitialCreate'
)
BEGIN
    CREATE TABLE [PasskeyCredentials] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [CredentialId] varbinary(1024) NOT NULL,
        [PublicKey] varbinary(max) NOT NULL,
        [SignCount] bigint NOT NULL,
        [Nickname] nvarchar(64) NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        CONSTRAINT [PK_PasskeyCredentials] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_PasskeyCredentials_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709175152_InitialCreate'
)
BEGIN
    CREATE TABLE [UserAvatars] (
        [UserId] uniqueidentifier NOT NULL,
        [Png] varbinary(max) NOT NULL,
        [UpdatedAtUtc] datetimeoffset NOT NULL,
        CONSTRAINT [PK_UserAvatars] PRIMARY KEY ([UserId]),
        CONSTRAINT [FK_UserAvatars_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709175152_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_ResourceId] ON [AuditLogs] ([ResourceId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709175152_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_TimestampUtc] ON [AuditLogs] ([TimestampUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709175152_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_AuditLogs_UserId] ON [AuditLogs] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709175152_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_PasskeyCredentials_CredentialId] ON [PasskeyCredentials] ([CredentialId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709175152_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_PasskeyCredentials_UserId] ON [PasskeyCredentials] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709175152_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Secrets_ExpiresAtUtc] ON [Secrets] ([ExpiresAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709175152_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Secrets_Name] ON [Secrets] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709175152_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Secrets_ProjectKey_Environment] ON [Secrets] ([ProjectKey], [Environment]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709175152_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_TrustedNetworks_Cidr] ON [TrustedNetworks] ([Cidr]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709175152_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_Username] ON [Users] ([Username]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709175152_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260709175152_InitialCreate', N'10.0.9');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709181823_AddSecretVersioning'
)
BEGIN
    CREATE TABLE [SecretVersions] (
        [Id] uniqueidentifier NOT NULL,
        [SecretId] uniqueidentifier NOT NULL,
        [VersionNumber] int NOT NULL,
        [Ciphertext] varbinary(max) NOT NULL,
        [WrappedDek] varbinary(max) NOT NULL,
        [KekId] nvarchar(64) NOT NULL,
        [Algorithm] nvarchar(32) NOT NULL,
        [ArchivedAtUtc] datetimeoffset NOT NULL,
        [ArchivedBy] nvarchar(64) NOT NULL,
        [ChangeNote] nvarchar(256) NULL,
        CONSTRAINT [PK_SecretVersions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SecretVersions_Secrets_SecretId] FOREIGN KEY ([SecretId]) REFERENCES [Secrets] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709181823_AddSecretVersioning'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SecretVersions_SecretId_VersionNumber] ON [SecretVersions] ([SecretId], [VersionNumber]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709181823_AddSecretVersioning'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260709181823_AddSecretVersioning', N'10.0.9');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709182709_AddAccountLockout'
)
BEGIN
    ALTER TABLE [Users] ADD [FailedAccessCount] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709182709_AddAccountLockout'
)
BEGIN
    ALTER TABLE [Users] ADD [LockedUntilUtc] datetimeoffset NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709182709_AddAccountLockout'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260709182709_AddAccountLockout', N'10.0.9');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709185006_AddUniqueEmailIndex'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_Email] ON [Users] ([Email]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709185006_AddUniqueEmailIndex'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260709185006_AddUniqueEmailIndex', N'10.0.9');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709190848_AddSecretGrants'
)
BEGIN
    CREATE TABLE [SecretGrants] (
        [Id] uniqueidentifier NOT NULL,
        [SecretId] uniqueidentifier NOT NULL,
        [GranteeUserId] uniqueidentifier NOT NULL,
        [GranteeUsername] nvarchar(64) NOT NULL,
        [GrantedBy] nvarchar(64) NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [ExpiresAtUtc] datetimeoffset NULL,
        CONSTRAINT [PK_SecretGrants] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_SecretGrants_Secrets_SecretId] FOREIGN KEY ([SecretId]) REFERENCES [Secrets] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_SecretGrants_Users_GranteeUserId] FOREIGN KEY ([GranteeUserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709190848_AddSecretGrants'
)
BEGIN
    CREATE INDEX [IX_SecretGrants_GranteeUserId] ON [SecretGrants] ([GranteeUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709190848_AddSecretGrants'
)
BEGIN
    CREATE UNIQUE INDEX [IX_SecretGrants_SecretId_GranteeUserId] ON [SecretGrants] ([SecretId], [GranteeUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709190848_AddSecretGrants'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260709190848_AddSecretGrants', N'10.0.9');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709193242_AddServiceAccounts'
)
BEGIN
    CREATE TABLE [ServiceAccounts] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(64) NOT NULL,
        [Clearance] int NOT NULL,
        [ProjectKey] nvarchar(64) NOT NULL,
        [IsDisabled] bit NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        CONSTRAINT [PK_ServiceAccounts] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709193242_AddServiceAccounts'
)
BEGIN
    CREATE TABLE [ApiKeys] (
        [Id] uniqueidentifier NOT NULL,
        [ServiceAccountId] uniqueidentifier NOT NULL,
        [KeyHash] nvarchar(64) NOT NULL,
        [Prefix] nvarchar(32) NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [ExpiresAtUtc] datetimeoffset NULL,
        [RevokedAtUtc] datetimeoffset NULL,
        [LastUsedAtUtc] datetimeoffset NULL,
        CONSTRAINT [PK_ApiKeys] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ApiKeys_ServiceAccounts_ServiceAccountId] FOREIGN KEY ([ServiceAccountId]) REFERENCES [ServiceAccounts] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709193242_AddServiceAccounts'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ApiKeys_KeyHash] ON [ApiKeys] ([KeyHash]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709193242_AddServiceAccounts'
)
BEGIN
    CREATE INDEX [IX_ApiKeys_ServiceAccountId] ON [ApiKeys] ([ServiceAccountId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709193242_AddServiceAccounts'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ServiceAccounts_Name] ON [ServiceAccounts] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260709193242_AddServiceAccounts'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260709193242_AddServiceAccounts', N'10.0.9');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711220117_AddApiKeyScopes'
)
BEGIN
    ALTER TABLE [ApiKeys] ADD [ClearanceCeiling] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711220117_AddApiKeyScopes'
)
BEGIN
    ALTER TABLE [ApiKeys] ADD [MetadataOnly] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711220117_AddApiKeyScopes'
)
BEGIN
    ALTER TABLE [ApiKeys] ADD [ProjectScope] nvarchar(64) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711220117_AddApiKeyScopes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260711220117_AddApiKeyScopes', N'10.0.9');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711222259_AddAccessRequests'
)
BEGIN
    CREATE TABLE [AccessRequests] (
        [Id] uniqueidentifier NOT NULL,
        [SecretId] uniqueidentifier NOT NULL,
        [SecretName] nvarchar(128) NOT NULL,
        [ProjectKey] nvarchar(64) NOT NULL,
        [RequesterUserId] uniqueidentifier NOT NULL,
        [RequesterUsername] nvarchar(64) NOT NULL,
        [Reason] nvarchar(500) NOT NULL,
        [DeniedReasons] nvarchar(2000) NULL,
        [Status] int NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [DecidedAtUtc] datetimeoffset NULL,
        [DecidedBy] nvarchar(64) NULL,
        [DecisionNote] nvarchar(500) NULL,
        CONSTRAINT [PK_AccessRequests] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AccessRequests_Secrets_SecretId] FOREIGN KEY ([SecretId]) REFERENCES [Secrets] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AccessRequests_Users_RequesterUserId] FOREIGN KEY ([RequesterUserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711222259_AddAccessRequests'
)
BEGIN
    CREATE INDEX [IX_AccessRequests_RequesterUserId] ON [AccessRequests] ([RequesterUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711222259_AddAccessRequests'
)
BEGIN
    CREATE INDEX [IX_AccessRequests_SecretId] ON [AccessRequests] ([SecretId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711222259_AddAccessRequests'
)
BEGIN
    CREATE INDEX [IX_AccessRequests_Status] ON [AccessRequests] ([Status]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260711222259_AddAccessRequests'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260711222259_AddAccessRequests', N'10.0.9');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260712132511_AddEmailLog'
)
BEGIN
    CREATE TABLE [EmailLogs] (
        [Id] uniqueidentifier NOT NULL,
        [ToAddress] nvarchar(256) NOT NULL,
        [Subject] nvarchar(256) NOT NULL,
        [Body] nvarchar(4000) NOT NULL,
        [EventType] nvarchar(64) NOT NULL,
        [Transport] nvarchar(32) NOT NULL,
        [Status] int NOT NULL,
        [Error] nvarchar(1024) NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        CONSTRAINT [PK_EmailLogs] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260712132511_AddEmailLog'
)
BEGIN
    CREATE INDEX [IX_EmailLogs_CreatedAtUtc] ON [EmailLogs] ([CreatedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260712132511_AddEmailLog'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260712132511_AddEmailLog', N'10.0.9');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260712135001_AddAuditChain'
)
BEGIN
    ALTER TABLE [AuditLogs] ADD [EntryHash] nvarchar(64) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260712135001_AddAuditChain'
)
BEGIN
    ALTER TABLE [AuditLogs] ADD [PreviousHash] nvarchar(64) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260712135001_AddAuditChain'
)
BEGIN
    ALTER TABLE [AuditLogs] ADD [Sequence] bigint NOT NULL DEFAULT CAST(0 AS bigint);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260712135001_AddAuditChain'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_AuditLogs_Sequence] ON [AuditLogs] ([Sequence]) WHERE [Sequence] <> 0');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260712135001_AddAuditChain'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260712135001_AddAuditChain', N'10.0.9');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713153740_AddMfaRecoveryCodes'
)
BEGIN
    CREATE TABLE [MfaRecoveryCodes] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [CodeHash] varbinary(64) NOT NULL,
        [Salt] varbinary(32) NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [UsedAtUtc] datetimeoffset NULL,
        CONSTRAINT [PK_MfaRecoveryCodes] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_MfaRecoveryCodes_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713153740_AddMfaRecoveryCodes'
)
BEGIN
    CREATE INDEX [IX_MfaRecoveryCodes_UserId_UsedAtUtc] ON [MfaRecoveryCodes] ([UserId], [UsedAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713153740_AddMfaRecoveryCodes'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260713153740_AddMfaRecoveryCodes', N'10.0.9');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713222122_AddAuditCheckpoints'
)
BEGIN
    CREATE TABLE [AuditCheckpoints] (
        [Id] uniqueidentifier NOT NULL,
        [Sequence] bigint NOT NULL,
        [ChainHeadHash] nvarchar(64) NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [Signature] varbinary(256) NOT NULL,
        [SigningKeyId] nvarchar(32) NOT NULL,
        CONSTRAINT [PK_AuditCheckpoints] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713222122_AddAuditCheckpoints'
)
BEGIN
    CREATE INDEX [IX_AuditCheckpoints_Sequence] ON [AuditCheckpoints] ([Sequence]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260713222122_AddAuditCheckpoints'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260713222122_AddAuditCheckpoints', N'10.0.9');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714081512_AddApiKeyIpAllowlist'
)
BEGIN
    ALTER TABLE [ApiKeys] ADD [AllowedCidrs] nvarchar(512) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260714081512_AddApiKeyIpAllowlist'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260714081512_AddApiKeyIpAllowlist', N'10.0.9');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260716100826_AddSecretExpiryNotice'
)
BEGIN
    ALTER TABLE [Secrets] ADD [ExpiryNoticeSentForUtc] datetimeoffset NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260716100826_AddSecretExpiryNotice'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260716100826_AddSecretExpiryNotice', N'10.0.9');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260716103631_AddDynamicSecrets'
)
BEGIN
    CREATE TABLE [DynamicSecretLeases] (
        [Id] uniqueidentifier NOT NULL,
        [RoleId] uniqueidentifier NOT NULL,
        [RoleName] nvarchar(64) NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [Username] nvarchar(64) NOT NULL,
        [CredentialIdentity] nvarchar(128) NOT NULL,
        [IssuedAtUtc] datetimeoffset NOT NULL,
        [ExpiresAtUtc] datetimeoffset NOT NULL,
        [ClosedAtUtc] datetimeoffset NULL,
        [Status] int NOT NULL,
        [RevocationError] nvarchar(500) NULL,
        CONSTRAINT [PK_DynamicSecretLeases] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260716103631_AddDynamicSecrets'
)
BEGIN
    CREATE TABLE [DynamicSecretRoles] (
        [Id] uniqueidentifier NOT NULL,
        [Name] nvarchar(64) NOT NULL,
        [Description] nvarchar(256) NOT NULL,
        [ProjectKey] nvarchar(64) NOT NULL,
        [Environment] int NOT NULL,
        [Sensitivity] int NOT NULL,
        [Backend] int NOT NULL,
        [CreationStatements] nvarchar(4000) NOT NULL,
        [RevocationStatements] nvarchar(4000) NOT NULL,
        [DefaultTtlMinutes] int NOT NULL,
        [MaxTtlMinutes] int NOT NULL,
        [IsEnabled] bit NOT NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        CONSTRAINT [PK_DynamicSecretRoles] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260716103631_AddDynamicSecrets'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DynamicSecretLeases_CredentialIdentity] ON [DynamicSecretLeases] ([CredentialIdentity]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260716103631_AddDynamicSecrets'
)
BEGIN
    CREATE INDEX [IX_DynamicSecretLeases_Status_ExpiresAtUtc] ON [DynamicSecretLeases] ([Status], [ExpiresAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260716103631_AddDynamicSecrets'
)
BEGIN
    CREATE INDEX [IX_DynamicSecretLeases_UserId] ON [DynamicSecretLeases] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260716103631_AddDynamicSecrets'
)
BEGIN
    CREATE UNIQUE INDEX [IX_DynamicSecretRoles_Name] ON [DynamicSecretRoles] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260716103631_AddDynamicSecrets'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260716103631_AddDynamicSecrets', N'10.0.9');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260716123951_AddManagedRotation'
)
BEGIN
    ALTER TABLE [Secrets] ADD [RotationBackend] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260716123951_AddManagedRotation'
)
BEGIN
    ALTER TABLE [Secrets] ADD [RotationPrincipal] nvarchar(128) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260716123951_AddManagedRotation'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260716123951_AddManagedRotation', N'10.0.9');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725182412_AddAuditHashVersion'
)
BEGIN
    ALTER TABLE [AuditLogs] ADD [HashVersion] int NOT NULL DEFAULT 1;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725182412_AddAuditHashVersion'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260725182412_AddAuditHashVersion', N'10.0.9');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725184239_AddLastTotpStep'
)
BEGIN
    ALTER TABLE [Users] ADD [LastTotpStep] bigint NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260725184239_AddLastTotpStep'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260725184239_AddLastTotpStep', N'10.0.9');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260907130000_AddFeatureEvaluations'
)
BEGIN
    CREATE TABLE [FeatureEvaluations] (
        [FeatureKey] nvarchar(64) NOT NULL,
        [StartedAtUtc] datetimeoffset NOT NULL,
        CONSTRAINT [PK_FeatureEvaluations] PRIMARY KEY ([FeatureKey])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260907130000_AddFeatureEvaluations'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260907130000_AddFeatureEvaluations', N'10.0.9');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020135348_AddShareIssuerUserId'
)
BEGIN
    ALTER TABLE [SecretGrants] ADD [IssuerUserId] uniqueidentifier NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020135348_AddShareIssuerUserId'
)
BEGIN
    CREATE INDEX [IX_SecretGrants_IssuerUserId] ON [SecretGrants] ([IssuerUserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020135348_AddShareIssuerUserId'
)
BEGIN
    UPDATE g
    SET IssuerUserId = u.Id
    FROM SecretGrants AS g
    JOIN Users AS u
        ON u.Username = g.GrantedBy
        AND u.CreatedAtUtc <= g.CreatedAtUtc;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261020135348_AddShareIssuerUserId'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261020135348_AddShareIssuerUserId', N'10.0.9');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022163039_NormalizeEmailIdentity'
)
BEGIN
    DECLARE @collisions nvarchar(max) = (
        SELECT STRING_AGG([Accounts], N'; ') FROM (
            SELECT STRING_AGG(CONVERT(nvarchar(max), [Id]), N' and ') WITHIN GROUP (ORDER BY [Id]) AS [Accounts]
            FROM [Users]
            GROUP BY TRANSLATE(TRIM(CONCAT(NCHAR(9), NCHAR(10), NCHAR(11), NCHAR(12), NCHAR(13), NCHAR(32), NCHAR(133), NCHAR(160), NCHAR(5760), NCHAR(8192), NCHAR(8193), NCHAR(8194), NCHAR(8195), NCHAR(8196), NCHAR(8197), NCHAR(8198), NCHAR(8199), NCHAR(8200), NCHAR(8201), NCHAR(8202), NCHAR(8232), NCHAR(8233), NCHAR(8239), NCHAR(8287), NCHAR(12288)) FROM [Email] COLLATE Latin1_General_100_BIN2), N'abcdefghijklmnopqrstuvwxyz', N'ABCDEFGHIJKLMNOPQRSTUVWXYZ')
            HAVING COUNT(*) > 1
            UNION ALL
            SELECT CONVERT(nvarchar(36), [named].[Id]) + N' (username) and ' + CONVERT(nvarchar(36), [addressed].[Id]) + N' (email)'
            FROM [Users] AS [named]
            JOIN [Users] AS [addressed]
                ON [named].[Id] <> [addressed].[Id]
                AND ([named].[Username] = [addressed].[Email]
                    OR TRANSLATE(TRIM(CONCAT(NCHAR(9), NCHAR(10), NCHAR(11), NCHAR(12), NCHAR(13), NCHAR(32), NCHAR(133), NCHAR(160), NCHAR(5760), NCHAR(8192), NCHAR(8193), NCHAR(8194), NCHAR(8195), NCHAR(8196), NCHAR(8197), NCHAR(8198), NCHAR(8199), NCHAR(8200), NCHAR(8201), NCHAR(8202), NCHAR(8232), NCHAR(8233), NCHAR(8239), NCHAR(8287), NCHAR(12288)) FROM [named].[Username] COLLATE Latin1_General_100_BIN2), N'abcdefghijklmnopqrstuvwxyz', N'ABCDEFGHIJKLMNOPQRSTUVWXYZ') = TRANSLATE(TRIM(CONCAT(NCHAR(9), NCHAR(10), NCHAR(11), NCHAR(12), NCHAR(13), NCHAR(32), NCHAR(133), NCHAR(160), NCHAR(5760), NCHAR(8192), NCHAR(8193), NCHAR(8194), NCHAR(8195), NCHAR(8196), NCHAR(8197), NCHAR(8198), NCHAR(8199), NCHAR(8200), NCHAR(8201), NCHAR(8202), NCHAR(8232), NCHAR(8233), NCHAR(8239), NCHAR(8287), NCHAR(12288)) FROM [addressed].[Email] COLLATE Latin1_General_100_BIN2), N'abcdefghijklmnopqrstuvwxyz', N'ABCDEFGHIJKLMNOPQRSTUVWXYZ'))
        ) AS [Shared]);
    IF @collisions IS NOT NULL
    BEGIN
        DECLARE @message nvarchar(2048) = LEFT(N'EclipsVault cannot upgrade: some email addresses would belong to more than one account. Email addresses now compare ignoring the case of letters A to Z and surrounding white space, and no username may be the email address of a different account. No account was changed. Give all but one account in each group below a different email address or username, then upgrade again. Accounts by id: ' + @collisions + N'.', 2048);
        THROW 50000, @message, 1;
    END
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022163039_NormalizeEmailIdentity'
)
BEGIN
    ALTER TABLE [Users] ADD [NormalizedEmail] nvarchar(256) COLLATE Latin1_General_100_BIN2 NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022163039_NormalizeEmailIdentity'
)
BEGIN
    UPDATE [Users] SET [NormalizedEmail] = TRANSLATE(TRIM(CONCAT(NCHAR(9), NCHAR(10), NCHAR(11), NCHAR(12), NCHAR(13), NCHAR(32), NCHAR(133), NCHAR(160), NCHAR(5760), NCHAR(8192), NCHAR(8193), NCHAR(8194), NCHAR(8195), NCHAR(8196), NCHAR(8197), NCHAR(8198), NCHAR(8199), NCHAR(8200), NCHAR(8201), NCHAR(8202), NCHAR(8232), NCHAR(8233), NCHAR(8239), NCHAR(8287), NCHAR(12288)) FROM [Email] COLLATE Latin1_General_100_BIN2), N'abcdefghijklmnopqrstuvwxyz', N'ABCDEFGHIJKLMNOPQRSTUVWXYZ');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022163039_NormalizeEmailIdentity'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Users]') AND [c].[name] = N'NormalizedEmail');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [Users] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [Users] ALTER COLUMN [NormalizedEmail] nvarchar(256) COLLATE Latin1_General_100_BIN2 NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022163039_NormalizeEmailIdentity'
)
BEGIN
    DROP INDEX [IX_Users_Email] ON [Users];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022163039_NormalizeEmailIdentity'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Users_NormalizedEmail] ON [Users] ([NormalizedEmail]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261022163039_NormalizeEmailIdentity'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261022163039_NormalizeEmailIdentity', N'10.0.9');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023113939_AddAccountConcurrencyStamp'
)
BEGIN
    ALTER TABLE [Users] ADD [ConcurrencyStamp] uniqueidentifier NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023113939_AddAccountConcurrencyStamp'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261023113939_AddAccountConcurrencyStamp', N'10.0.9');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024165337_AddCredentialGeneration'
)
BEGIN
    ALTER TABLE [Users] ADD [CredentialGeneration] bigint NOT NULL DEFAULT CAST(0 AS bigint);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261024165337_AddCredentialGeneration'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261024165337_AddCredentialGeneration', N'10.0.9');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028052345_AddProfileOperations'
)
BEGIN
    CREATE TABLE [ProfileOperations] (
        [Id] uniqueidentifier NOT NULL,
        [UserId] uniqueidentifier NOT NULL,
        [SessionId] uniqueidentifier NOT NULL,
        [CredentialGeneration] bigint NOT NULL,
        [RevocationVersion] nvarchar(64) NOT NULL,
        [Kind] int NOT NULL,
        [CandidatePasswordHash] varbinary(64) NULL,
        [CandidatePasswordSalt] varbinary(32) NULL,
        [PasskeyNickname] nvarchar(64) NULL,
        [TargetPasskeyId] uniqueidentifier NULL,
        [CreatedAtUtc] datetimeoffset NOT NULL,
        [ExpiresAtUtc] datetimeoffset NOT NULL,
        [VerifiedAtUtc] datetimeoffset NULL,
        [ProofMethod] int NULL,
        [Challenge] nvarchar(128) NULL,
        [ChallengePurpose] int NULL,
        [ChallengeExpiresAtUtc] datetimeoffset NULL,
        CONSTRAINT [PK_ProfileOperations] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_ProfileOperations_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028052345_AddProfileOperations'
)
BEGIN
    CREATE UNIQUE INDEX [IX_ProfileOperations_UserId_SessionId] ON [ProfileOperations] ([UserId], [SessionId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028052345_AddProfileOperations'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261028052345_AddProfileOperations', N'10.0.9');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028074344_AddMailboxVerification'
)
BEGIN
    ALTER TABLE [ProfileOperations] ADD [MailboxCodeHash] varbinary(32) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028074344_AddMailboxVerification'
)
BEGIN
    ALTER TABLE [ProfileOperations] ADD [MailboxDelivered] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028074344_AddMailboxVerification'
)
BEGIN
    ALTER TABLE [ProfileOperations] ADD [MailboxExpiresAtUtc] datetimeoffset NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028074344_AddMailboxVerification'
)
BEGIN
    ALTER TABLE [ProfileOperations] ADD [MailboxFailures] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028074344_AddMailboxVerification'
)
BEGIN
    ALTER TABLE [ProfileOperations] ADD [TargetEmail] nvarchar(254) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028074344_AddMailboxVerification'
)
BEGIN
    CREATE TABLE [EmailVerificationSends] (
        [Id] uniqueidentifier NOT NULL,
        [AccountId] uniqueidentifier NOT NULL,
        [SourceHash] nvarchar(64) NOT NULL,
        [SentAtUtc] datetimeoffset NOT NULL,
        CONSTRAINT [PK_EmailVerificationSends] PRIMARY KEY ([Id])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028074344_AddMailboxVerification'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [IX_ProfileOperations_UserId] ON [ProfileOperations] ([UserId]) WHERE [Kind] = 6');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028074344_AddMailboxVerification'
)
BEGIN
    CREATE INDEX [IX_EmailVerificationSends_AccountId_SentAtUtc] ON [EmailVerificationSends] ([AccountId], [SentAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028074344_AddMailboxVerification'
)
BEGIN
    CREATE INDEX [IX_EmailVerificationSends_SourceHash_SentAtUtc] ON [EmailVerificationSends] ([SourceHash], [SentAtUtc]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261028074344_AddMailboxVerification'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261028074344_AddMailboxVerification', N'10.0.9');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030224901_ClearPendingDenialReasons'
)
BEGIN
    UPDATE AccessRequests
    SET DeniedReasons = NULL
    WHERE Status = 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261030224901_ClearPendingDenialReasons'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261030224901_ClearPendingDenialReasons', N'10.0.9');
END;
GO

COMMIT;
GO


-- stale: not produced by the regenerate command
