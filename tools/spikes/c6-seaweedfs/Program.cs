// C6 spike probe: the evidence adapter's exact client settings and calls, against SeaweedFS.
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

string url = args[0], key = args[1], secret = args[2], mode = args.Length > 3 ? args[3] : "full";
AmazonS3Client Client(AWSCredentials credentials) => new(credentials, new AmazonS3Config
{
    ServiceURL = url, ForcePathStyle = true, AuthenticationRegion = "us-east-1",
});
var s3 = Client(new BasicAWSCredentials(key, secret));
const string bucket = "devbuddy-spike";
const string marker = "DEVBUDDY-SPIKE-PLAINTEXT-MARKER-0123456789";
int failures = 0;
void Check(string name, bool ok, string detail = "") { Console.WriteLine($"{(ok ? "PASS" : "FAIL")} {name} {detail}"); if (!ok) failures++; }

if (mode == "read")
{
    try
    {
        using var again = await s3.GetObjectAsync(bucket, "persist/object.txt");
        string text = await new StreamReader(again.ResponseStream).ReadToEndAsync();
        Check("read after restart", text.Contains(marker));
    }
    catch (Exception e) { Check("read after restart", false, e.GetType().Name + ": " + e.Message); }
    return failures;
}

var buckets = await s3.ListBucketsAsync();
if (buckets.Buckets?.Exists(b => b.BucketName == bucket) != true)
    await s3.PutBucketAsync(new PutBucketRequest { BucketName = bucket });
buckets = await s3.ListBucketsAsync();
Check("PutBucket + ListBuckets", buckets.Buckets?.Exists(b => b.BucketName == bucket) == true);

byte[] body = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(marker + "\n", 20000))); // ~860 KB
foreach (string name in new[] { "small/object.txt", "persist/object.txt" })
{
    var put = new PutObjectRequest
    {
        BucketName = bucket, Key = name, InputStream = new MemoryStream(name.StartsWith("small") ? Encoding.UTF8.GetBytes(marker) : body),
        ContentType = "text/plain", ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256,
    };
    var r = await s3.PutObjectAsync(put);
    Check($"PutObject SSE-S3 {name}", r.ServerSideEncryptionMethod == ServerSideEncryptionMethod.AES256, $"sse={r.ServerSideEncryptionMethod}");
}

var head = await s3.GetObjectMetadataAsync(bucket, "persist/object.txt");
Check("HEAD reports AES256", head.ServerSideEncryptionMethod == ServerSideEncryptionMethod.AES256, $"sse={head.ServerSideEncryptionMethod} len={head.ContentLength}");

using (var get = await s3.GetObjectAsync(bucket, "persist/object.txt"))
{
    using var ms = new MemoryStream(); await get.ResponseStream.CopyToAsync(ms);
    Check("GetObject round-trips bytes", ms.ToArray().SequenceEqual(body), $"len={ms.Length}");
}

await s3.DeleteObjectAsync(bucket, "small/object.txt");
try { await s3.GetObjectAsync(bucket, "small/object.txt"); Check("DeleteObject", false, "still readable"); }
catch (AmazonS3Exception e) { Check("DeleteObject", e.StatusCode == System.Net.HttpStatusCode.NotFound, $"status={(int)e.StatusCode}"); }

try { await Client(new AnonymousAWSCredentials()).ListBucketsAsync(); Check("anonymous refused", false, "anonymous listed buckets"); }
catch (AmazonS3Exception e) { Check("anonymous refused", (int)e.StatusCode is 403 or 401, $"status={(int)e.StatusCode}"); }

try { await Client(new BasicAWSCredentials(key, secret + "x")).ListBucketsAsync(); Check("wrong secret refused", false); }
catch (AmazonS3Exception e) { Check("wrong secret refused", (int)e.StatusCode == 403, $"status={(int)e.StatusCode}"); }

return failures;
