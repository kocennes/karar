const crypto = require('node:crypto');
const functions = require('@google-cloud/functions-framework');
const {SecretManagerServiceClient} = require('@google-cloud/secret-manager');

const client = new SecretManagerServiceClient();

const currentTokenSecret = process.env.ADMIN_TOKEN_SECRET || 'admin-token';
const previousTokenSecret =
  process.env.ADMIN_PREVIOUS_TOKEN_SECRET || 'admin-previous-token';

functions.cloudEvent('rotateAdminAuth', async () => {
  const projectId =
    process.env.GOOGLE_CLOUD_PROJECT ||
    process.env.GCP_PROJECT ||
    process.env.PROJECT_ID;
  if (!projectId) throw new Error('PROJECT_ID is required.');

  const currentToken = await readLatestSecret(projectId, currentTokenSecret);
  if (currentToken) {
    await addSecretVersion(projectId, previousTokenSecret, currentToken);
  }

  const nextToken = `karar-admin-${crypto.randomBytes(48).toString('base64url')}`;
  await addSecretVersion(projectId, currentTokenSecret, nextToken);

  console.log('Rotated admin auth token and preserved previous token.');
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
