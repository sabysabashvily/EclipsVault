import type { components } from '../ui-api/schema.generated';

export type Profile = components['schemas']['ProfileResponse'];
export type PreparedOperation = components['schemas']['PreparedOperationResponse'];
export type VerifiedOperation = components['schemas']['VerifiedOperationResponse'];
export type PrepareChange = components['schemas']['PrepareProfileOperationRequest'];
export type ProofMethod = PreparedOperation['methods'][number];
export type PendingEmail = components['schemas']['PendingEmailResponse'];
export type ProfilePasskey = Profile['passkeys'][number];
export type NewRecoveryCodes = components['schemas']['RecoveryCodesResponse'];
export type PasskeyOptions = components['schemas']['PasskeyOptionsResponse'];
export type RegisteredPasskey = components['schemas']['RegisteredPasskeyResponse'];

export function refusalReason(code: string | null): string {
  switch (code) {
    case 'authenticator_not_enrolled':
      return 'This needs an authenticator app set up first.';
    case 'alternate_method_required':
      return 'Another usable sign-in method must remain after this change.';
    case 'mail_unavailable':
      return 'Email changes require configured, enabled SMTP delivery.';
    default:
      return 'This change requires an existing local credential to verify your account.';
  }
}
