#!/usr/bin/env bash
set -euo pipefail

# One-time grant: gives quotes-bff's system-assigned Managed Identity
# (created by resources.bicep) the quotes-api-day17 Entra app registration's
# "Api.Invoke" application role, so its Managed-Identity-acquired access
# token (see ../Program.cs) is one QuotesApi's Entra JWT scheme will accept.
#
# ARM/Bicep has no native resource type for an Entra app role assignment
# (Microsoft.Graph is not an ARM provider), so this is a plain Microsoft
# Graph call instead of a bicep resource. Wired up as this azd project's
# `postprovision` hook (see ../azure.yaml), it only runs after `azd provision`
# has actually created quotes-bff and its identity - which Day 17 Part 3
# deliberately does not do yet ("Do not deploy yet"). Nothing in this
# repository invokes this script on its own.
#
# The two IDs below are not guessed - they were read directly from this
# exercise's own Azure AD tenant (see result.md, Day 17 Part 3):
#   - QUOTES_API_APP_SP_OBJECT_ID: the service principal (enterprise
#     application) object ID for the "quotes-api-day17" app registration
#     that exposes the Api.Invoke role, found via
#     `az ad sp show --id 729a2be3-9609-4fd1-b7c5-e658386f9bfd --query id`.
#   - API_INVOKE_APP_ROLE_ID: that app registration's own "Api.Invoke"
#     app role ID, found via
#     `az ad app show --id 729a2be3-9609-4fd1-b7c5-e658386f9bfd --query appRoles`.
QUOTES_API_APP_SP_OBJECT_ID="43050566-7eed-4220-a1b7-8b2533204239"
API_INVOKE_APP_ROLE_ID="428f70d1-ea0c-44d8-914a-9234db5dae42"

BFF_PRINCIPAL_ID="$(az containerapp show \
  --name quotes-bff \
  --resource-group thinkschool-rg \
  --query identity.principalId -o tsv)"

# Idempotent: re-running this after the role assignment already exists
# returns a 400 (duplicate) from Graph, which is treated as success rather
# than failing the postprovision hook.
if az rest --method post \
  --url "https://graph.microsoft.com/v1.0/servicePrincipals/${QUOTES_API_APP_SP_OBJECT_ID}/appRoleAssignedTo" \
  --headers 'Content-Type=application/json' \
  --body "{\"principalId\":\"${BFF_PRINCIPAL_ID}\",\"resourceId\":\"${QUOTES_API_APP_SP_OBJECT_ID}\",\"appRoleId\":\"${API_INVOKE_APP_ROLE_ID}\"}" \
  > /tmp/grant-api-invoke-role.out 2>&1; then
  echo "Granted Api.Invoke to quotes-bff (${BFF_PRINCIPAL_ID})."
else
  if grep -qi "Permission being assigned already exists" /tmp/grant-api-invoke-role.out; then
    echo "quotes-bff (${BFF_PRINCIPAL_ID}) already has Api.Invoke - nothing to do."
  else
    cat /tmp/grant-api-invoke-role.out >&2
    exit 1
  fi
fi
