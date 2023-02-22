﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Fhir.Loader.Tool
{
    public class UploadConfiguration
    {
        [JsonProperty("fhirServerUrl")]
        public string FhirServerUrl { get; set; }

        [JsonProperty("useFhirAuthentication")]
        public bool UseFhirAuthentication { get; set; }

        [JsonProperty("blobListFile")]
        public string BlobListFile { get; set; }

        [JsonProperty("readBlobConcurrency")]
        public int ReadBlobConcurrency { get; set; }

        [JsonProperty("putFhirConcurrency")]
        public int PutFhirConcurrency { get; set; }

        [JsonProperty("tenantId")]
        public string TenantId { get; set; }

        [JsonProperty("clientId")]
        public string ClientId { get; set; }

        [JsonProperty("clientSecret")]
        public string ClientSecret { get; set; }
    }
}
