# C6 spike: SeaweedFS as the evidence store

Run once on 2026-09-26 on devrelease (LXC 101, amd64) and kept as the record of what was checked.
Nothing here is built by CI or shipped. `docs/plan-phase-14.md`, C6, has the conclusions.

- `Dockerfile`: SeaweedFS 4.47 (`c5073360007d28385a33426a42ac3e4ec504c5a3`) from source with Go
  1.26.8 pinned by digest, into `scratch`, uid 1000, the same shape as `docker/evidence/Dockerfile`.
- `Program.cs`: the evidence adapter's exact client settings (`AmazonS3Config` with `ServiceURL`,
  `ForcePathStyle`, `AuthenticationRegion = "us-east-1"`, SDK defaults otherwise) and its calls
  (`ListBuckets`, `PutBucket`, `PutObject` with `AES256`, `GetObject`, `DeleteObject`), plus HEAD,
  anonymous and wrong-secret checks. Built as a console project referencing `AWSSDK.S3` 4.0.102.4,
  the version `Directory.Packages.props` pins; the project file is not kept because central package
  management would refuse it here.

The server ran as `weed -logtostderr=true server -dir=/srv/evidence -s3`, read-only, every
capability dropped, `no-new-privileges`, a tmpfs on `/tmp` owned by uid 1000, credentials in
`AWS_ACCESS_KEY_ID` and `AWS_SECRET_ACCESS_KEY`, and the SSE-S3 key in `WEED_S3_SSE_KEY`.
