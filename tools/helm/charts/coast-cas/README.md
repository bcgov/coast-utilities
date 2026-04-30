# COAST CAS Helm Chart

This directory contains a Helm chart to deploy the CAS Interface Service.

## Usage

To install a new environment, ensure the values file matches the environment, then run:

```sh
helm -n [namespace] install -f envs/[env]/values.yaml [release-name] .
```

To upgrade an existing environment:

```sh
helm -n [namespace] upgrade -f envs/[env]/values.yaml [release-name] .
```

## Image Trigger Deployments

This chart uses OpenShift ImageStreamTag triggers on the Deployment. When a new
image is pushed to the registry with the configured tag, OpenShift will
automatically roll out the new image without requiring a Helm upgrade.

Helm upgrades are only needed when configuration or template changes are made.

## Environment Values

Per-environment values files are stored in `envs/<env>/values.yaml` (gitignored).
See the defaults section below for required values.

## Cleanup of Legacy DeploymentConfig

After deploying via this Helm chart, remove the legacy objects:

```sh
oc delete dc cas-interface-service -n [namespace]
oc delete bc cas-interface-service -n [namespace]
oc delete is cas-interface-service -n [namespace]
```
