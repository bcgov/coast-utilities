# Image Scanning (Grype + Trivy) for coast-utilities

Build each service image locally with Podman and scan it with Grype and Trivy to confirm no CRITICAL/HIGH vulnerabilities remain. Run from WSL (Ubuntu).

| App            | Dockerfile                                           | Local tag              |
| -------------- | ---------------------------------------------------- | ---------------------- |
| AEM            | `AEMInterfaceService/AEMInterfaceService/Dockerfile` | `aem-service:local`    |
| CAS            | `CASInterfaceService/CASInterfaceService/Dockerfile` | `cas-service:local`    |
| Cornet         | `CornetInterfaceService/Dockerfile`                  | `cornet-service:local` |
| job-scheduling | `job-scheduling/job-scheduling/openshift/Dockerfile` | `job-scheduling:local` |

The build context is always the repo root (`coast-utilities/`); every Dockerfile does `COPY . .`.

---

## 1. Prerequisites (one-time)

```bash
# OC CLI (only needed to scan images already pushed to OpenShift)
cp /mnt/c/Users/<YourUsername>/Downloads/oc.tar ~/ && cd ~ && tar -xvf oc.tar
sudo mv oc /usr/local/bin/ && sudo chmod +x /usr/local/bin/oc

# Grype
curl -sSfL https://raw.githubusercontent.com/anchore/grype/main/install.sh | sh -s -- -b /usr/local/bin

# Trivy
curl -sfL https://raw.githubusercontent.com/aquasecurity/trivy/main/contrib/install.sh | sh -s -- -b /usr/local/bin

grype version; trivy --version
```

## 2. Podman socket (every new WSL session)

```bash
podman system service --time=0 unix:///run/user/$(id -u)/podman/podman.sock &
export DOCKER_HOST=unix:///run/user/$(id -u)/podman/podman.sock
```

Persist:

```bash
echo 'export DOCKER_HOST=unix:///run/user/$(id -u)/podman/podman.sock' >> ~/.bashrc
echo 'podman system service --time=0 unix:///run/user/$(id -u)/podman/podman.sock &' >> ~/.bashrc
```

## 3. Build one image (repo root)

```bash
cd /mnt/c/git/DevOps/COAST/coast-utilities

# AEM
podman build -t aem-service:local \
  -f AEMInterfaceService/AEMInterfaceService/Dockerfile \
  --build-arg BUILD_ID=local --build-arg BUILD_VERSION=local-dev .

# CAS
podman build -t cas-service:local \
  -f CASInterfaceService/CASInterfaceService/Dockerfile \
  --build-arg BUILD_ID=local --build-arg BUILD_VERSION=local-dev .

# Cornet
podman build -t cornet-service:local \
  -f CornetInterfaceService/Dockerfile \
  --build-arg BUILD_ID=local --build-arg BUILD_VERSION=local-dev .

# job-scheduling
podman build -t job-scheduling:local \
  -f job-scheduling/job-scheduling/openshift/Dockerfile \
  --build-arg BUILD_ID=local --build-arg BUILD_VERSION=local-dev .
```

## 4. Scan with Grype

```bash
APP=aem-service   # aem-service | cas-service | cornet-service | job-scheduling

podman save $APP:local -o ~/$APP-local.tar
grype docker-archive:$HOME/$APP-local.tar --only-fixed --fail-on high -o table > ~/$APP-grype.txt
cat ~/$APP-grype.txt
```

Show only CRITICAL/HIGH:

```bash
grype docker-archive:$HOME/$APP-local.tar -o table | grep -E 'NAME|Critical|High'
```

## 5. Scan with Trivy (vulnerabilities + secrets + misconfig)

```bash
trivy image --input ~/$APP-local.tar \
  --scanners vuln,secret,misconfig \
  --severity CRITICAL,HIGH \
  --format table -o ~/$APP-trivy.txt
cat ~/$APP-trivy.txt
```

Trivy downloads its DB on first run; pre-cache with `trivy image --download-db-only`.

## 6. Scan an image already in OpenShift

```bash
oc login https://api.silver.devops.gov.bc.ca:6443 --token=<your-token>
podman login -u $(oc whoami) -p $(oc whoami --show-token) image-registry.apps.silver.devops.gov.bc.ca

IMAGE="image-registry.apps.silver.devops.gov.bc.ca/<namespace>/<image-name>@sha256:<digest>"
podman pull $IMAGE
grype $IMAGE
trivy image --scanners vuln,secret,misconfig $IMAGE
```

The token expires; re-run `oc login` and `podman login` when it does.

## 7. Known fixes applied (November 2026)

- CVE-2026-88092 / CVE-2026-70188 (ASP.NET Core runtime 10.0.10): fixed in 10.0.13+. Dockerfiles pin `aspnet:10.0-alpine`, `sdk:10.0` and `runtime:10.0` (job-scheduling) digests that contain .NET 10.0.15.
- When a new .NET patch ships, update the pinned digests in all four Dockerfiles together.
- A pinned digest only picks up fixes when it is bumped manually; re-scan after each bump.
- OpenSSL (`libssl3`/`libcrypto3`) High findings: the AEM, CAS and Cornet final stages run `apk upgrade --no-cache` so Alpine patches newer than the pinned base image are applied at build time. job-scheduling (Ubuntu 24.04 base) was clean without this.
- Use `grype --only-fixed` to list findings that can actually be fixed. Remaining High findings without a fixed version (5 OpenSSL CVEs on the Alpine images) must be re-checked when Alpine ships a patch.
