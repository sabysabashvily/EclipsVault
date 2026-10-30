import { PasskeyCreation } from './passkey-creation';
import { PasskeyProof } from './passkey-proof';

/** Stands in for the browser's credential: only what the prompts read from it. */
class BrowserCredential {
  constructor(private readonly json: unknown) {}
  toJSON(): unknown {
    return this.json;
  }
}

describe('passkey prompts', () => {
  const credentials = { get: vi.fn(), create: vi.fn() };
  beforeEach(() => {
    vi.stubGlobal(
      'PublicKeyCredential',
      Object.assign(BrowserCredential, {
        parseRequestOptionsFromJSON: (options: unknown) => options,
        parseCreationOptionsFromJSON: (options: unknown) => options,
      }),
    );
    vi.stubGlobal('navigator', { credentials });
  });
  afterEach(() => {
    vi.unstubAllGlobals();
    credentials.get.mockReset();
    credentials.create.mockReset();
  });

  it('answers a proof with the assertion fields beside the credential id', async () => {
    credentials.get.mockResolvedValue(
      new BrowserCredential({
        id: 'credential-id',
        rawId: 'credential-id',
        type: 'public-key',
        response: {
          clientDataJSON: 'client-data',
          authenticatorData: 'authenticator-data',
          signature: 'signature',
          userHandle: 'user-handle',
        },
        clientExtensionResults: {},
      }),
    );

    const answer = await new PasskeyProof().get({}, new AbortController().signal);

    expect(answer).toEqual({
      id: 'credential-id',
      clientDataJSON: 'client-data',
      authenticatorData: 'authenticator-data',
      signature: 'signature',
    });
  });

  it('answers a registration with the attestation fields beside the credential id', async () => {
    credentials.create.mockResolvedValue(
      new BrowserCredential({
        id: 'credential-id',
        rawId: 'credential-id',
        type: 'public-key',
        response: {
          clientDataJSON: 'client-data',
          attestationObject: 'attestation',
          authenticatorData: 'authenticator-data',
          transports: ['internal'],
          publicKeyAlgorithm: -7,
        },
        clientExtensionResults: {},
      }),
    );

    const answer = await new PasskeyCreation().create({}, new AbortController().signal);

    expect(answer).toEqual({
      id: 'credential-id',
      clientDataJSON: 'client-data',
      attestationObject: 'attestation',
    });
  });

  it('hands the browser the caller abort signal', async () => {
    credentials.create.mockReturnValue(new Promise(() => undefined));
    const prompt = new AbortController();

    void new PasskeyCreation().create({ challenge: 'server-challenge' }, prompt.signal);

    expect(credentials.create).toHaveBeenCalledExactlyOnceWith({
      publicKey: { challenge: 'server-challenge' },
      signal: prompt.signal,
    });
  });
});
