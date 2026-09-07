# Screenshots

Four PNGs under `screenshots/`, generated from real, freshly re-run command output (not fabricated, not mocked). Each is a Playwright/Chromium screenshot of an HTML page whose only content is the literal text a real command printed - the page is styled to look like a terminal window, but every character of output shown is real.

## 01-bicep-validation.png
`az bicep build --file main.bicep`, `az bicep lint --file main.bicep`, and `az bicep build-params` for both `dev.bicepparam`/`prod.bicepparam`. All five show `Exit code: 0` with no errors or warnings - the same clean validation recorded in `bicep-validation.txt`.

## 02-dev-what-if.png
`az deployment group what-if` against `thinkschool-rg` with `params/dev.bicepparam`. Ends with **`Resource changes: 8 to create, 18 to ignore.`** - re-run fresh for this screenshot and confirmed identical to the original `dev-what-if.txt` result: creates `quotes-api-dev`, `sb-quotesapi-dev` + topic + 2 subscriptions, `sql-quotesapi-dev` + database + firewall rule. No modifies, no deletes, no resources were actually created.

## 03-prod-what-if.png
`az deployment group what-if` against `thinkschool-rg` with `params/prod.bicepparam`. Ends with **`Resource changes: 4 to modify, 17 to ignore.`** - re-run fresh and confirmed identical to the original `prod-what-if.txt` result. No creates, no deletes; nothing was applied. **One value is redacted** in this screenshot: the live `quotes-api` Container App's `APPLICATIONINSIGHTS_CONNECTION_STRING` env var (an Application Insights ingestion key) is replaced with a visible `[REDACTED - ...]` marker, called out explicitly on the page itself. No other secret, token, password, or access key appears - every `@secure()` parameter (`jwtSigningKey`, `redisConnectionString`, `appInsightsConnectionString`) only ever appears in what-if output as an unresolved ARM template expression (e.g. `parameters('jwtSigningKey')`), never a resolved value, both here and in the underlying `az` output.

## 04-azure-resources.png
`az resource list --resource-group thinkschool-rg --output table`, re-run fresh. Shows all 18 real resources in the resource group, including `thinkschool-env`, `quotes-api`, `quotes-bff`, `cr2i2oapij4zsrc`, `sb-quotesapi-thinkschool`, `redis-quotesapi-thinkschool`, `stday20quotesapi`, `thinkschool-ayush-swa`, and the unrelated Day-7 SQL server `thinkschool-day7-sql-0c0dda` - identical to the original resource list.

## How they were generated

A temporary, local-only Playwright setup under `day-23/evidence-tools/` (Node.js + the `playwright` npm package + a downloaded Chromium binary) rendered each captured text file into a styled HTML page and screenshotted it. That tooling was removed after the four PNGs above were generated and verified - it was never part of the Day 23 IaC deliverable itself, only the means of turning already-real command output into image evidence.
