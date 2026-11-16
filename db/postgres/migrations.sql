CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE TABLE "AuditCheckpoints" (
        "Id" uuid NOT NULL,
        "Sequence" bigint NOT NULL,
        "ChainHeadHash" character varying(64) NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "Signature" bytea NOT NULL,
        "SigningKeyId" character varying(32) NOT NULL,
        CONSTRAINT "PK_AuditCheckpoints" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE TABLE "AuditLogs" (
        "Id" uuid NOT NULL,
        "TimestampUtc" timestamp with time zone NOT NULL,
        "UserId" uuid,
        "Username" character varying(64) NOT NULL,
        "SourceIp" character varying(64) NOT NULL,
        "Action" integer NOT NULL,
        "ResourceType" character varying(64) NOT NULL,
        "ResourceId" uuid,
        "ResourceName" character varying(128),
        "Details" character varying(1024),
        "IsCritical" boolean NOT NULL,
        "Sequence" bigint NOT NULL,
        "PreviousHash" character varying(64),
        "EntryHash" character varying(64),
        CONSTRAINT "PK_AuditLogs" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE TABLE "DynamicSecretLeases" (
        "Id" uuid NOT NULL,
        "RoleId" uuid NOT NULL,
        "RoleName" character varying(64) NOT NULL,
        "UserId" uuid NOT NULL,
        "Username" character varying(64) NOT NULL,
        "CredentialIdentity" character varying(128) NOT NULL,
        "IssuedAtUtc" timestamp with time zone NOT NULL,
        "ExpiresAtUtc" timestamp with time zone NOT NULL,
        "ClosedAtUtc" timestamp with time zone,
        "Status" integer NOT NULL,
        "RevocationError" character varying(500),
        CONSTRAINT "PK_DynamicSecretLeases" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE TABLE "DynamicSecretRoles" (
        "Id" uuid NOT NULL,
        "Name" character varying(64) NOT NULL,
        "Description" character varying(256) NOT NULL,
        "ProjectKey" character varying(64) NOT NULL,
        "Environment" integer NOT NULL,
        "Sensitivity" integer NOT NULL,
        "Backend" integer NOT NULL,
        "CreationStatements" character varying(4000) NOT NULL,
        "RevocationStatements" character varying(4000) NOT NULL,
        "DefaultTtlMinutes" integer NOT NULL,
        "MaxTtlMinutes" integer NOT NULL,
        "IsEnabled" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_DynamicSecretRoles" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE TABLE "EmailLogs" (
        "Id" uuid NOT NULL,
        "ToAddress" character varying(256) NOT NULL,
        "Subject" character varying(256) NOT NULL,
        "Body" character varying(4000) NOT NULL,
        "EventType" character varying(64) NOT NULL,
        "Transport" character varying(32) NOT NULL,
        "Status" integer NOT NULL,
        "Error" character varying(1024),
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_EmailLogs" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE TABLE "Secrets" (
        "Id" uuid NOT NULL,
        "Name" character varying(128) NOT NULL,
        "ProjectKey" character varying(64) NOT NULL,
        "Environment" integer NOT NULL,
        "Sensitivity" integer NOT NULL,
        "Ciphertext" bytea NOT NULL,
        "WrappedDek" bytea NOT NULL,
        "KekId" character varying(64) NOT NULL,
        "Algorithm" character varying(32) NOT NULL,
        "IsHoneyToken" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UpdatedAtUtc" timestamp with time zone,
        "ExpiresAtUtc" timestamp with time zone,
        "ExpiryNoticeSentForUtc" timestamp with time zone,
        "IsShredded" boolean NOT NULL,
        "CreatedByUserId" uuid NOT NULL,
        "RotationBackend" integer,
        "RotationPrincipal" character varying(128),
        CONSTRAINT "PK_Secrets" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE TABLE "ServiceAccounts" (
        "Id" uuid NOT NULL,
        "Name" character varying(64) NOT NULL,
        "Clearance" integer NOT NULL,
        "ProjectKey" character varying(64) NOT NULL,
        "IsDisabled" boolean NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_ServiceAccounts" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE TABLE "TrustedNetworks" (
        "Id" uuid NOT NULL,
        "Cidr" character varying(64) NOT NULL,
        "Label" character varying(128) NOT NULL,
        "AddedBy" character varying(64) NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_TrustedNetworks" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE TABLE "Users" (
        "Id" uuid NOT NULL,
        "Username" character varying(64) NOT NULL,
        "DisplayName" character varying(64) NOT NULL,
        "Email" character varying(256) NOT NULL,
        "PasswordHash" bytea NOT NULL,
        "PasswordSalt" bytea NOT NULL,
        "TotpSecret" character varying(128),
        "TotpEnabled" boolean NOT NULL,
        "Clearance" integer NOT NULL,
        "ProjectKey" character varying(64) NOT NULL,
        "IsDisabled" boolean NOT NULL,
        "FailedAccessCount" integer NOT NULL,
        "LockedUntilUtc" timestamp with time zone,
        "AvatarUpdatedAtUtc" timestamp with time zone,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_Users" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE TABLE "SecretVersions" (
        "Id" uuid NOT NULL,
        "SecretId" uuid NOT NULL,
        "VersionNumber" integer NOT NULL,
        "Ciphertext" bytea NOT NULL,
        "WrappedDek" bytea NOT NULL,
        "KekId" character varying(64) NOT NULL,
        "Algorithm" character varying(32) NOT NULL,
        "ArchivedAtUtc" timestamp with time zone NOT NULL,
        "ArchivedBy" character varying(64) NOT NULL,
        "ChangeNote" character varying(256),
        CONSTRAINT "PK_SecretVersions" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_SecretVersions_Secrets_SecretId" FOREIGN KEY ("SecretId") REFERENCES "Secrets" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE TABLE "ApiKeys" (
        "Id" uuid NOT NULL,
        "ServiceAccountId" uuid NOT NULL,
        "KeyHash" character varying(64) NOT NULL,
        "Prefix" character varying(32) NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "ExpiresAtUtc" timestamp with time zone,
        "RevokedAtUtc" timestamp with time zone,
        "LastUsedAtUtc" timestamp with time zone,
        "ClearanceCeiling" integer,
        "ProjectScope" character varying(64),
        "MetadataOnly" boolean NOT NULL DEFAULT FALSE,
        "AllowedCidrs" character varying(512),
        CONSTRAINT "PK_ApiKeys" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_ApiKeys_ServiceAccounts_ServiceAccountId" FOREIGN KEY ("ServiceAccountId") REFERENCES "ServiceAccounts" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE TABLE "AccessRequests" (
        "Id" uuid NOT NULL,
        "SecretId" uuid NOT NULL,
        "SecretName" character varying(128) NOT NULL,
        "ProjectKey" character varying(64) NOT NULL,
        "RequesterUserId" uuid NOT NULL,
        "RequesterUsername" character varying(64) NOT NULL,
        "Reason" character varying(500) NOT NULL,
        "DeniedReasons" character varying(2000),
        "Status" integer NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "DecidedAtUtc" timestamp with time zone,
        "DecidedBy" character varying(64),
        "DecisionNote" character varying(500),
        CONSTRAINT "PK_AccessRequests" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_AccessRequests_Secrets_SecretId" FOREIGN KEY ("SecretId") REFERENCES "Secrets" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_AccessRequests_Users_RequesterUserId" FOREIGN KEY ("RequesterUserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE TABLE "MfaRecoveryCodes" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "CodeHash" bytea NOT NULL,
        "Salt" bytea NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "UsedAtUtc" timestamp with time zone,
        CONSTRAINT "PK_MfaRecoveryCodes" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_MfaRecoveryCodes_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE TABLE "PasskeyCredentials" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "CredentialId" bytea NOT NULL,
        "PublicKey" bytea NOT NULL,
        "SignCount" bigint NOT NULL,
        "Nickname" character varying(64),
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_PasskeyCredentials" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_PasskeyCredentials_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE TABLE "SecretGrants" (
        "Id" uuid NOT NULL,
        "SecretId" uuid NOT NULL,
        "GranteeUserId" uuid NOT NULL,
        "GranteeUsername" character varying(64) NOT NULL,
        "GrantedBy" character varying(64) NOT NULL,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "ExpiresAtUtc" timestamp with time zone,
        CONSTRAINT "PK_SecretGrants" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_SecretGrants_Secrets_SecretId" FOREIGN KEY ("SecretId") REFERENCES "Secrets" ("Id") ON DELETE CASCADE,
        CONSTRAINT "FK_SecretGrants_Users_GranteeUserId" FOREIGN KEY ("GranteeUserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE TABLE "UserAvatars" (
        "UserId" uuid NOT NULL,
        "Png" bytea NOT NULL,
        "UpdatedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_UserAvatars" PRIMARY KEY ("UserId"),
        CONSTRAINT "FK_UserAvatars_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE INDEX "IX_AccessRequests_RequesterUserId" ON "AccessRequests" ("RequesterUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE INDEX "IX_AccessRequests_SecretId" ON "AccessRequests" ("SecretId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE INDEX "IX_AccessRequests_Status" ON "AccessRequests" ("Status");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE UNIQUE INDEX "IX_ApiKeys_KeyHash" ON "ApiKeys" ("KeyHash");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE INDEX "IX_ApiKeys_ServiceAccountId" ON "ApiKeys" ("ServiceAccountId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE INDEX "IX_AuditCheckpoints_Sequence" ON "AuditCheckpoints" ("Sequence");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE INDEX "IX_AuditLogs_ResourceId" ON "AuditLogs" ("ResourceId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE UNIQUE INDEX "IX_AuditLogs_Sequence" ON "AuditLogs" ("Sequence") WHERE "Sequence" <> 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE INDEX "IX_AuditLogs_TimestampUtc" ON "AuditLogs" ("TimestampUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE INDEX "IX_AuditLogs_UserId" ON "AuditLogs" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE UNIQUE INDEX "IX_DynamicSecretLeases_CredentialIdentity" ON "DynamicSecretLeases" ("CredentialIdentity");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE INDEX "IX_DynamicSecretLeases_Status_ExpiresAtUtc" ON "DynamicSecretLeases" ("Status", "ExpiresAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE INDEX "IX_DynamicSecretLeases_UserId" ON "DynamicSecretLeases" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE UNIQUE INDEX "IX_DynamicSecretRoles_Name" ON "DynamicSecretRoles" ("Name");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE INDEX "IX_EmailLogs_CreatedAtUtc" ON "EmailLogs" ("CreatedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE INDEX "IX_MfaRecoveryCodes_UserId_UsedAtUtc" ON "MfaRecoveryCodes" ("UserId", "UsedAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE UNIQUE INDEX "IX_PasskeyCredentials_CredentialId" ON "PasskeyCredentials" ("CredentialId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE INDEX "IX_PasskeyCredentials_UserId" ON "PasskeyCredentials" ("UserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE INDEX "IX_SecretGrants_GranteeUserId" ON "SecretGrants" ("GranteeUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE UNIQUE INDEX "IX_SecretGrants_SecretId_GranteeUserId" ON "SecretGrants" ("SecretId", "GranteeUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE INDEX "IX_Secrets_ExpiresAtUtc" ON "Secrets" ("ExpiresAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE UNIQUE INDEX "IX_Secrets_Name" ON "Secrets" ("Name");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE INDEX "IX_Secrets_ProjectKey_Environment" ON "Secrets" ("ProjectKey", "Environment");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE UNIQUE INDEX "IX_SecretVersions_SecretId_VersionNumber" ON "SecretVersions" ("SecretId", "VersionNumber");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE UNIQUE INDEX "IX_ServiceAccounts_Name" ON "ServiceAccounts" ("Name");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE UNIQUE INDEX "IX_TrustedNetworks_Cidr" ON "TrustedNetworks" ("Cidr");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE UNIQUE INDEX "IX_Users_Email" ON "Users" ("Email");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    CREATE UNIQUE INDEX "IX_Users_Username" ON "Users" ("Username");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260716154841_InitialPostgres') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260716154841_InitialPostgres', '10.0.9');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260725182425_AddAuditHashVersion') THEN
    ALTER TABLE "AuditLogs" ADD "HashVersion" integer NOT NULL DEFAULT 1;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260725182425_AddAuditHashVersion') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260725182425_AddAuditHashVersion', '10.0.9');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260725184241_AddLastTotpStep') THEN
    ALTER TABLE "Users" ADD "LastTotpStep" bigint;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260725184241_AddLastTotpStep') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260725184241_AddLastTotpStep', '10.0.9');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907130029_AddFeatureEvaluations') THEN
    CREATE TABLE "FeatureEvaluations" (
        "FeatureKey" character varying(64) NOT NULL,
        "StartedAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_FeatureEvaluations" PRIMARY KEY ("FeatureKey")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20260907130029_AddFeatureEvaluations') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20260907130029_AddFeatureEvaluations', '10.0.9');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020135354_AddShareIssuerUserId') THEN
    ALTER TABLE "SecretGrants" ADD "IssuerUserId" uuid;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020135354_AddShareIssuerUserId') THEN
    CREATE INDEX "IX_SecretGrants_IssuerUserId" ON "SecretGrants" ("IssuerUserId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020135354_AddShareIssuerUserId') THEN
    UPDATE "SecretGrants" AS g
    SET "IssuerUserId" = u."Id"
    FROM "Users" AS u
    WHERE u."Username" = g."GrantedBy"
        AND u."CreatedAtUtc" <= g."CreatedAtUtc";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261020135354_AddShareIssuerUserId') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261020135354_AddShareIssuerUserId', '10.0.9');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261022163043_NormalizeEmailIdentity') THEN
    DO $$
    DECLARE
        collisions text;
    BEGIN
        SELECT string_agg(accounts, '; ') INTO collisions FROM (
            SELECT string_agg("Id"::text, ' and ' ORDER BY "Id") AS accounts
            FROM "Users"
            GROUP BY translate(btrim("Email", concat(chr(9), chr(10), chr(11), chr(12), chr(13), chr(32), chr(133), chr(160), chr(5760), chr(8192), chr(8193), chr(8194), chr(8195), chr(8196), chr(8197), chr(8198), chr(8199), chr(8200), chr(8201), chr(8202), chr(8232), chr(8233), chr(8239), chr(8287), chr(12288))), 'abcdefghijklmnopqrstuvwxyz', 'ABCDEFGHIJKLMNOPQRSTUVWXYZ') COLLATE "C"
            HAVING count(*) > 1
            UNION ALL
            SELECT named."Id"::text || ' (username) and ' || addressed."Id"::text || ' (email)'
            FROM "Users" AS named
            JOIN "Users" AS addressed
                ON named."Id" <> addressed."Id"
                AND (named."Username" = addressed."Email"
                    OR translate(btrim(named."Username", concat(chr(9), chr(10), chr(11), chr(12), chr(13), chr(32), chr(133), chr(160), chr(5760), chr(8192), chr(8193), chr(8194), chr(8195), chr(8196), chr(8197), chr(8198), chr(8199), chr(8200), chr(8201), chr(8202), chr(8232), chr(8233), chr(8239), chr(8287), chr(12288))), 'abcdefghijklmnopqrstuvwxyz', 'ABCDEFGHIJKLMNOPQRSTUVWXYZ') COLLATE "C" = translate(btrim(addressed."Email", concat(chr(9), chr(10), chr(11), chr(12), chr(13), chr(32), chr(133), chr(160), chr(5760), chr(8192), chr(8193), chr(8194), chr(8195), chr(8196), chr(8197), chr(8198), chr(8199), chr(8200), chr(8201), chr(8202), chr(8232), chr(8233), chr(8239), chr(8287), chr(12288))), 'abcdefghijklmnopqrstuvwxyz', 'ABCDEFGHIJKLMNOPQRSTUVWXYZ') COLLATE "C")
        ) AS shared;
        IF collisions IS NOT NULL THEN
            RAISE EXCEPTION '%', 'EclipsVault cannot upgrade: some email addresses would belong to more than one account. Email addresses now compare ignoring the case of letters A to Z and surrounding white space, and no username may be the email address of a different account. No account was changed. Give all but one account in each group below a different email address or username, then upgrade again. Accounts by id: ' || collisions || '.';
        END IF;
    END $$;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261022163043_NormalizeEmailIdentity') THEN
    ALTER TABLE "Users" ADD "NormalizedEmail" character varying(256) COLLATE "C";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261022163043_NormalizeEmailIdentity') THEN
    UPDATE "Users" SET "NormalizedEmail" = translate(btrim("Email", concat(chr(9), chr(10), chr(11), chr(12), chr(13), chr(32), chr(133), chr(160), chr(5760), chr(8192), chr(8193), chr(8194), chr(8195), chr(8196), chr(8197), chr(8198), chr(8199), chr(8200), chr(8201), chr(8202), chr(8232), chr(8233), chr(8239), chr(8287), chr(12288))), 'abcdefghijklmnopqrstuvwxyz', 'ABCDEFGHIJKLMNOPQRSTUVWXYZ') COLLATE "C";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261022163043_NormalizeEmailIdentity') THEN
    ALTER TABLE "Users" ALTER COLUMN "NormalizedEmail" SET NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261022163043_NormalizeEmailIdentity') THEN
    DROP INDEX "IX_Users_Email";
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261022163043_NormalizeEmailIdentity') THEN
    CREATE UNIQUE INDEX "IX_Users_NormalizedEmail" ON "Users" ("NormalizedEmail");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261022163043_NormalizeEmailIdentity') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261022163043_NormalizeEmailIdentity', '10.0.9');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023113949_AddAccountConcurrencyStamp') THEN
    ALTER TABLE "Users" ADD "ConcurrencyStamp" uuid NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261023113949_AddAccountConcurrencyStamp') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261023113949_AddAccountConcurrencyStamp', '10.0.9');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024165340_AddCredentialGeneration') THEN
    ALTER TABLE "Users" ADD "CredentialGeneration" bigint NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261024165340_AddCredentialGeneration') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261024165340_AddCredentialGeneration', '10.0.9');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028052352_AddProfileOperations') THEN
    CREATE TABLE "ProfileOperations" (
        "Id" uuid NOT NULL,
        "UserId" uuid NOT NULL,
        "SessionId" uuid NOT NULL,
        "CredentialGeneration" bigint NOT NULL,
        "RevocationVersion" character varying(64) NOT NULL,
        "Kind" integer NOT NULL,
        "CandidatePasswordHash" bytea,
        "CandidatePasswordSalt" bytea,
        "PasskeyNickname" character varying(64),
        "TargetPasskeyId" uuid,
        "CreatedAtUtc" timestamp with time zone NOT NULL,
        "ExpiresAtUtc" timestamp with time zone NOT NULL,
        "VerifiedAtUtc" timestamp with time zone,
        "ProofMethod" integer,
        "Challenge" character varying(128),
        "ChallengePurpose" integer,
        "ChallengeExpiresAtUtc" timestamp with time zone,
        CONSTRAINT "PK_ProfileOperations" PRIMARY KEY ("Id"),
        CONSTRAINT "FK_ProfileOperations_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028052352_AddProfileOperations') THEN
    CREATE UNIQUE INDEX "IX_ProfileOperations_UserId_SessionId" ON "ProfileOperations" ("UserId", "SessionId");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028052352_AddProfileOperations') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261028052352_AddProfileOperations', '10.0.9');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028074454_AddMailboxVerification') THEN
    ALTER TABLE "ProfileOperations" ADD "MailboxCodeHash" bytea;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028074454_AddMailboxVerification') THEN
    ALTER TABLE "ProfileOperations" ADD "MailboxDelivered" boolean NOT NULL DEFAULT FALSE;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028074454_AddMailboxVerification') THEN
    ALTER TABLE "ProfileOperations" ADD "MailboxExpiresAtUtc" timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028074454_AddMailboxVerification') THEN
    ALTER TABLE "ProfileOperations" ADD "MailboxFailures" integer NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028074454_AddMailboxVerification') THEN
    ALTER TABLE "ProfileOperations" ADD "TargetEmail" character varying(254);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028074454_AddMailboxVerification') THEN
    CREATE TABLE "EmailVerificationSends" (
        "Id" uuid NOT NULL,
        "AccountId" uuid NOT NULL,
        "SourceHash" character varying(64) NOT NULL,
        "SentAtUtc" timestamp with time zone NOT NULL,
        CONSTRAINT "PK_EmailVerificationSends" PRIMARY KEY ("Id")
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028074454_AddMailboxVerification') THEN
    CREATE UNIQUE INDEX "IX_ProfileOperations_UserId" ON "ProfileOperations" ("UserId") WHERE "Kind" = 6;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028074454_AddMailboxVerification') THEN
    CREATE INDEX "IX_EmailVerificationSends_AccountId_SentAtUtc" ON "EmailVerificationSends" ("AccountId", "SentAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028074454_AddMailboxVerification') THEN
    CREATE INDEX "IX_EmailVerificationSends_SourceHash_SentAtUtc" ON "EmailVerificationSends" ("SourceHash", "SentAtUtc");
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261028074454_AddMailboxVerification') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261028074454_AddMailboxVerification', '10.0.9');
    END IF;
END $EF$;
COMMIT;

START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261030224907_ClearPendingDenialReasons') THEN
    UPDATE "AccessRequests"
    SET "DeniedReasons" = NULL
    WHERE "Status" = 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261030224907_ClearPendingDenialReasons') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261030224907_ClearPendingDenialReasons', '10.0.9');
    END IF;
END $EF$;
COMMIT;

