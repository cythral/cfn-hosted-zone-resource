using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Amazon.Route53;
using Amazon.Route53.Model;

using Cythral.CloudFormation.CustomResource.Core;
using Cythral.CloudFormation.CustomResource.Attributes;
using Cythral.CloudFormation.Resources.Factories;

using static Amazon.Route53.ChangeStatus;

namespace Cythral.CloudFormation.Resources
{

    /// <summary>
    /// Route 53 Hosted Zone Custom Resource accepting a DelegationSetId property
    /// </summary>
    [CustomResource]
    public partial class HostedZone
    {
        #region Inner Classes

        /// <summary>
        /// Resource properties for Hosted Zones.
        /// </summary>
        public class Properties
        {
            [UpdateRequiresReplacement]
            [Required]
            public string Name { get; set; } = null!;

            [UpdateRequiresReplacement]
            public string DelegationSetId { get; set; } = null!;

            public HostedZoneConfig? HostedZoneConfig { get; set; }

            public QueryLoggingConfig? QueryLoggingConfig { get; set; }

            public List<Tag>? HostedZoneTags { get; set; } = null;

            public List<VPC>? VPCs { get; set; } = null;
        }

        /// <summary>
        /// Data returned to CloudFormation
        /// </summary>
        public class Data
        {
            public string Id = "";
        }

        #endregion

        #region Fields & Properties

        /// <summary>
        /// Client used to make API calls to Route53
        /// </summary>
        /// <returns>Route 53 Client</returns>
        private Route53Factory route53Factory = new Route53Factory();

        /// <summary>
        /// Tags that have been updated or inserted since creation or last update
        /// </summary>
        /// <value></value>
        public IEnumerable<Tag> UpsertedTags
        {
            get
            {
                var prev = from tag in Request.OldResourceProperties?.HostedZoneTags ?? new List<Tag>()
                           select new { Key = tag.Key, Value = tag.Value };

                var curr = from tag in Request.ResourceProperties?.HostedZoneTags ?? new List<Tag>()
                           select new { Key = tag.Key, Value = tag.Value };

                return from tag in curr.Except(prev)
                       select new Tag { Key = tag.Key, Value = tag.Value };
            }
        }

        /// <summary>
        /// Tags that were deleted since creation or last update
        /// </summary>
        /// <value>List of names of deleted tags</value>
        public IEnumerable<string> DeletedTags
        {
            get
            {
                var oldKeys = from tag in Request.OldResourceProperties?.HostedZoneTags ?? new List<Tag>() select tag.Key;
                var newKeys = from tag in Request.ResourceProperties?.HostedZoneTags ?? new List<Tag>() select tag.Key;

                return oldKeys.Except(newKeys);
            }
        }

        public IEnumerable<VPC> AssociatableVPCs
        {
            get
            {
                var oldVpcs = from vpc in Request.OldResourceProperties?.VPCs ?? new List<VPC>()
                              select new { VPCId = vpc.VPCId, VPCRegion = vpc.VPCRegion };

                var newVpcs = from vpc in Request.ResourceProperties?.VPCs ?? new List<VPC>()
                              select new { VPCId = vpc.VPCId, VPCRegion = vpc.VPCRegion };

                return from vpc in newVpcs.Except(oldVpcs)
                       select new VPC { VPCId = vpc.VPCId, VPCRegion = vpc.VPCRegion };
            }
        }

        public IEnumerable<VPC> DisassociatableVPCs
        {
            get
            {
                var oldVpcs = from vpc in Request.OldResourceProperties?.VPCs ?? new List<VPC>()
                              select new { VPCId = vpc.VPCId, VPCRegion = vpc.VPCRegion };

                var newVpcs = from vpc in Request.ResourceProperties?.VPCs ?? new List<VPC>()
                              select new { VPCId = vpc.VPCId, VPCRegion = vpc.VPCRegion };

                return from vpc in oldVpcs.Except(newVpcs)
                       select new VPC { VPCId = vpc.VPCId, VPCRegion = vpc.VPCRegion };
            }
        }

        #endregion

        #region Methods
        /// <summary>
        /// Creates a new Hosted Zone in Route 53
        /// </summary>
        /// <returns>Response to send back to CloudFormation</returns>
        public async Task<Response> Create()
        {

            async Task DoCreateQueryLoggingConfig(Properties props, Data data)
            {
                if (props.QueryLoggingConfig != null)
                {
                    await CreateQueryLoggingConfig(props.QueryLoggingConfig.CloudWatchLogsLogGroupArn, data.Id);
                }
            }

            async Task DoCreateTags(Properties props, Data data)
            {
                if (props.HostedZoneTags != null)
                {
                    var client = route53Factory.Create();
                    var tagsResponse = await client.ChangeTagsForResourceAsync(new ChangeTagsForResourceRequest
                    {
                        ResourceId = data.Id,
                        ResourceType = "hostedzone",
                        AddTags = props.HostedZoneTags,
                    });

                    Console.WriteLine("Create Tags Response: " + JsonSerializer.Serialize(tagsResponse));
                }
            }

            void DoAssociateVpcs(Properties props, Data data)
            {
                if (props.VPCs != null && props.VPCs.Count() > 1)
                {
                    var vpcs = props.VPCs.Skip(1);
                    AssociateVPCs(vpcs.ToList(), data.Id);
                }
            }

            var props = Request.ResourceProperties;
            var request = new CreateHostedZoneRequest
            {
                CallerReference = DateTime.Now.ToString(),
                Name = Request.ResourceProperties.Name
            };

            if (props.DelegationSetId != null) request.DelegationSetId = props.DelegationSetId;
            if (props.HostedZoneConfig != null) request.HostedZoneConfig = props.HostedZoneConfig;
            if (props.VPCs != null) request.VPC = props.VPCs.First();

            Console.WriteLine(JsonSerializer.Serialize(request));

            var client = route53Factory.Create();
            var createHostedZoneResponse = await client.CreateHostedZoneAsync(request);
            var data = new Data { Id = createHostedZoneResponse.HostedZone.Id };

            PhysicalResourceId = data.Id;
            Console.WriteLine(JsonSerializer.Serialize(createHostedZoneResponse));

            // wait until the hosted zone finishes creating
            var getChangeRequest = new GetChangeRequest { Id = createHostedZoneResponse.ChangeInfo.Id };
            while ((await client.GetChangeAsync(getChangeRequest)).ChangeInfo.Status == PENDING)
            {
                var wait = 1;
                Console.WriteLine($"Create hosted zone still pending... sleeping {wait} seconds");
                Thread.Sleep(wait * 1000);
            }

            Task.WaitAll(new Task[] {
                Task.Run(() => DoCreateQueryLoggingConfig(props, data)),
                Task.Run(() => DoCreateTags(props, data)),
                Task.Run(() => DoAssociateVpcs(props, data)),
            });

            return new Response
            {
                Data = data
            };
        }


        /// <summary>
        /// Updates a HostedZone in Route 53
        /// </summary>
        /// <returns>Response to send back to CloudFormation</returns>
        public Task<Response> Update()
        {
            async Task DoUpdateTags()
            {
                if (UpsertedTags.Count() > 0 || DeletedTags.Count() > 0)
                {
                    Console.WriteLine("Updating Resource Tags");

                    var client = route53Factory.Create();
                    var tagsResponse = await client.ChangeTagsForResourceAsync(new ChangeTagsForResourceRequest
                    {
                        AddTags = UpsertedTags.ToList(),
                        RemoveTagKeys = DeletedTags.ToList(),
                        ResourceId = Request.PhysicalResourceId,
                        ResourceType = "hostedzone"
                    });

                    Console.WriteLine("Update Tags Response: " + JsonSerializer.Serialize(tagsResponse));
                }
            }

            void DoUpdateVpcs()
            {
                if (AssociatableVPCs.Count() > 0)
                {
                    Console.WriteLine("Associating new VPCs");
                    AssociateVPCs(AssociatableVPCs.ToList(), Request.PhysicalResourceId);
                }

                if (DisassociatableVPCs.Count() > 0)
                {
                    Console.WriteLine("Disassociating old VPCs");
                    DisassociateVPCs(DisassociatableVPCs.ToList(), Request.PhysicalResourceId);
                }
            }

            async Task DoUpdateComment(Properties oldProps, Properties newProps)
            {
                if (oldProps?.HostedZoneConfig?.Comment != newProps?.HostedZoneConfig?.Comment)
                {
                    Console.WriteLine($"Updating the HostedZone Comment");
                    var comment = (newProps?.HostedZoneConfig?.Comment) ?? "";
                    var id = Request.PhysicalResourceId;
                    var client = route53Factory.Create();
                    var updateCommentResponse = await client.UpdateHostedZoneCommentAsync(
                        new UpdateHostedZoneCommentRequest
                        {
                            Comment = comment,
                            Id = id,
                        }
                    );

                    var serialization = JsonSerializer.Serialize(updateCommentResponse);
                    Console.WriteLine($"Update Hosted Zone Comment Response: {serialization}");
                }
            }

            async Task DoUpdateQueryLoggingConfig(Properties oldProps, Properties newProps)
            {
                var oldGroup = oldProps?.QueryLoggingConfig?.CloudWatchLogsLogGroupArn;
                var newGroup = newProps?.QueryLoggingConfig?.CloudWatchLogsLogGroupArn;

                if (oldGroup != newGroup)
                {
                    Console.WriteLine("Deleting the old Hosted Zone Query Logging Config");
                    await DeleteQueryLoggingConfig(Request.PhysicalResourceId);

                    if (newGroup != null)
                    {
                        Console.WriteLine("Creating new Hosted Zone Query Logging Config");
                        await CreateQueryLoggingConfig(newGroup, Request.PhysicalResourceId);
                    }
                }
            }

            var oldProps = Request.OldResourceProperties;
            var newProps = Request.ResourceProperties;

            Task.WaitAll(new Task[] {
                Task.Run(() => DoUpdateTags()),
                Task.Run(() => DoUpdateVpcs()),
                Task.Run(() => DoUpdateComment(oldProps, newProps)),
                Task.Run(() => DoUpdateQueryLoggingConfig(oldProps, newProps))
            });

            return Task.FromResult(new Response
            {
                PhysicalResourceId = Request.PhysicalResourceId,
                Data = new Data()
            });
        }

        /// <summary>
        /// Deletes a HostedZone in Route 53
        /// </summary>
        /// <returns></returns>
        public async Task<Response> Delete()
        {
            var client = route53Factory.Create();
            var result = await client.DeleteHostedZoneAsync(new DeleteHostedZoneRequest
            {
                Id = Request.PhysicalResourceId
            });

            return new Response
            {
                Data = result
            };
        }

        private void AssociateVPCs(List<VPC> vpcs, string hostedZoneId)
        {
            var vpcTasks = new List<Task>();

            foreach (var vpc in vpcs)
            {
                vpcTasks.Add(
                    Task.Run(async delegate
                    {
                        var vpcRequest = new AssociateVPCWithHostedZoneRequest
                        {
                            Comment = "",
                            HostedZoneId = hostedZoneId,
                            VPC = vpc,
                        };

                        var client = route53Factory.Create();
                        var vpcResponse = await client.AssociateVPCWithHostedZoneAsync(vpcRequest);
                        Console.WriteLine($"Associate VPC Response: {JsonSerializer.Serialize(vpcResponse)}");
                    })
                );
            }

            Task.WaitAll(vpcTasks.ToArray());
        }

        private void DisassociateVPCs(List<VPC> vpcs, string hostedZoneId)
        {
            var vpcTasks = new List<Task>();

            foreach (var vpc in vpcs)
            {
                vpcTasks.Add(
                    Task.Run(async delegate
                    {
                        var vpcRequest = new DisassociateVPCFromHostedZoneRequest
                        {
                            Comment = "",
                            HostedZoneId = hostedZoneId,
                            VPC = vpc
                        };

                        var client = route53Factory.Create();
                        var vpcResponse = await client.DisassociateVPCFromHostedZoneAsync(vpcRequest);
                        Console.WriteLine($"Disassociate VPC Response: {JsonSerializer.Serialize(vpcResponse)}");
                    })
                );
            }

            Task.WaitAll(vpcTasks.ToArray());
        }

        private async Task<string> CreateQueryLoggingConfig(string groupArn, string hostedZoneId)
        {
            var client = route53Factory.Create();
            var queryLoggingResponse = await client.CreateQueryLoggingConfigAsync(new CreateQueryLoggingConfigRequest
            {
                CloudWatchLogsLogGroupArn = groupArn,
                HostedZoneId = hostedZoneId,
            });

            Console.WriteLine($"Create Query Logging Config Response: {JsonSerializer.Serialize(queryLoggingResponse)}");
            return queryLoggingResponse.QueryLoggingConfig.Id;
        }

        private async Task DeleteQueryLoggingConfig(string hostedZoneId)
        {
            var client = route53Factory.Create();
            var listConfigResponse = await client.ListQueryLoggingConfigsAsync(new ListQueryLoggingConfigsRequest
            {
                HostedZoneId = Request.PhysicalResourceId,
                MaxResults = "1"
            });

            var configId = listConfigResponse.QueryLoggingConfigs.First()?.Id;
            Console.WriteLine($"List Query Logging Config Response: {JsonSerializer.Serialize(listConfigResponse)}");

            if (configId == null)
            {
                Console.WriteLine("No Query Logging Config to delete.");
                return;
            }

            var deleteConfigResp = await client.DeleteQueryLoggingConfigAsync(new DeleteQueryLoggingConfigRequest
            {
                Id = configId
            });

            Console.WriteLine($"Delete Query Logging Config Response: {JsonSerializer.Serialize(deleteConfigResp)}");
        }
    }
    #endregion
}
