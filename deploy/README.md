# Release image

`Dockerfile` packages the embedded Vite frontend and .NET 10 host using a
multi-stage build. The runtime runs as the .NET image's non-root application
user, exposes port 8080 and keeps data/keys out of the image.

The release workflow builds `linux/amd64`, publishes to
`ghcr.io/metjuperry/tangle`, and passes the exact image digest to reusable CD.
CI builds the same Dockerfile without publishing and smoke-tests `/healthz`
using fake GitHub settings. The Docker build context excludes development
secrets, databases, keys, node_modules and build artifacts.

**This is the app image, not the completed M9 cloud runtime.** Its current
entry point starts the host directly. It does not yet acquire a Blob lease,
restore/replicate every user SQLite file or persist wrapped Data Protection keys.
Do not enable `TANGLE_CLOUD_RUNTIME_VERIFIED` for this Dockerfile as it stands.
The release deployment explicitly fails that gate until the durable runtime
is implemented. Building/publishing an image does not establish durability.
