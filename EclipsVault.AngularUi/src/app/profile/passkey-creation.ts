import { Injectable } from '@angular/core';

/** Creates a passkey through the browser's UV prompt, answering in the flat shape the vault reads. */
@Injectable({ providedIn: 'root' })
export class PasskeyCreation {
  readonly supported = typeof PublicKeyCredential !== 'undefined';

  async create(options: unknown, signal: AbortSignal): Promise<unknown> {
    const publicKey = PublicKeyCredential.parseCreationOptionsFromJSON(
      options as PublicKeyCredentialCreationOptionsJSON,
    );
    const credential = await navigator.credentials.create({ publicKey, signal });
    if (!(credential instanceof PublicKeyCredential)) throw new Error('Passkey cancelled');
    const { id, response } = credential.toJSON() as RegistrationResponseJSON;
    return {
      id,
      clientDataJSON: response.clientDataJSON,
      attestationObject: response.attestationObject,
    };
  }
}
