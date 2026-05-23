const crypto = require('node:crypto');
const functions = require('@google-cloud/functions-framework');
const {SecretManagerServiceClient} = require('@google-cloud/secret-manager');

const client = new SecretManagerServiceClient();

const secretIds = {
  privateKey: process.env.JWT_PRIVATE_KEY_SECRET || 'jwt-private-key-pem',
  publicKey: process.env.JWT_PUBLIC_KEY_SECRET || 'jwt-public-key-pem',
  keyId: process.env.JWT_KEY_ID_SECRET || 'jwt-key-id',
  previousPublicKey:
    process.env.JWT_PREVIOUS_PUBLIC_KEY_SECRET || 'jwt-previous-public-key-pem',
  previousKeyId: process.env.JWT_PREVIOUS_KEY_ID_SECRET || 'jwt-previous-key-id',
};

functions.cloudEvent('rotateJwtKey', async (cloudEvent) => {
  const projectId =
    process.env.GOOGLE_CLOUD_PROJECT ||
    process.env.GCP_PROJECT ||
    process.env.PROJECT_ID;
  if (!projectId) throw new Error('PROJECT_ID is required.');

  const currentPublicKey = await readLatestSecret(projectId, secretIds.publicKey);
  const currentKeyId = await readLatestSecret(projectId, secretIds.keyId);
  const nextKeyId = `jwt-${new Date().toISOString().slice(0, 10)}`;
  if (currentKeyId === nextKeyId) {
    console.log(`JWT key ${nextKeyId} is already active.`);
    return;
  }

  const {publicKey, privateKey} = crypto.generateKeyPairSync('rsa', {
    modulusLength: 3072,
    publicKeyEncoding: {
      type: 'spki',
      format: 'pem',
    },
    privateKeyEncoding: {
      type: 'pkcs8',
      format: 'pem',
    },
  });

  if (currentPublicKey) {
    await addSecretVersion(projectId, secretIds.previousPublicKey, currentPublicKey);
  }
  if (currentKeyId) {
    await addSecretVersion(projectId, secretIds.previousKeyId, currentKeyId);
  }

  await addSecretVersion(projectId, secretIds.privateKey, privateKey);
  await addSecretVersion(projectId, secretIds.publicKey, publicKey);
  await addSecretVersion(projectId, secretIds.keyId, nextKeyId);

  console.log(`Rotated JWT signing key to ${nextKeyId}.`);
});

async function readLatestSecret(projectId, secretId) {
  try {
    const [version] = await client.accessSecretVersion({
      name: `projects/${projectId}/secrets/${secretId}/versions/latest`,
    });
    return version.payload.data.toString('utf8').trim();
  } catch (error) {
    if (error.code === 5) return '';
    throw error;
  }
}

async function addSecretVersion(projectId, secretId, value) {
  await ensureSecret(projectId, secretId);
  await client.addSecretVersion({
    parent: `projects/${projectId}/secrets/${secretId}`,
    payload: {
      data: Buffer.from(value, 'utf8'),
    },
  });
}

async function ensureSecret(projectId, secretId) {
  const name = `projects/${projectId}/secrets/${secretId}`;
  try {
    await client.getSecret({name});
  } catch (error) {
    if (error.code !== 5) throw error;
    await client.createSecret({
      parent: `projects/${projectId}`,
      secretId,
      secret: {
        replication: {
          automatic: {},
        },
      },
    });
  }
}
