import { Injectable } from '@angular/core';

/**
 * The browser owns the UV prompt; no credential response survives the caller's request. The vault
 * reads the response's base64url fields beside the credential id, as the Legacy passkey calls send them.
 */
@Injectable({ providedIn: 'root' })
export class PasskeyProof {
  async get(options: unknown, signal: AbortSignal): Promise<unknown> {
    const publicKey = PublicKeyCredential.parseRequestOptionsFromJSON(
      options as PublicKeyCredentialRequestOptionsJSON,
    );
    const credential = await navigator.credentials.get({ publicKey, signal });
    if (!(credential instanceof PublicKeyCredential)) throw new Error('Passkey cancelled');
    const { id, response } = credential.toJSON() as AuthenticationResponseJSON;
    return {
      id,
      clientDataJSON: response.clientDataJSON,
      authenticatorData: response.authenticatorData,
      signature: response.signature,
    };
  }
}
