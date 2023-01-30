using System;
using System.Threading;
//using Microsoft.Rest;
using k8s;
using k8s.Models;
using System.Collections.Generic;
using System.Threading.Tasks;
using k8s.Autorest;
using System.ComponentModel;
using System.Linq;
using System.Xml.Linq;
using k8s;

namespace complete
{
    using CreateLambda = Func<IKubernetes, IKubernetesObject, string, CancellationToken, Task<IKubernetesObject>>;
    using DeleteLambda = Func<IKubernetes, string, string, CancellationToken, Task<IKubernetesObject>>;

   
    public class Program
    {
        /// <summary>
        /// CreateMethods is a dictionary of methods, used to find the appropriate create method based on the object Kind
        /// </summary>
        private static readonly Dictionary<string, CreateLambda> CreateMethods = new Dictionary<string, CreateLambda>()
        {
            [V1ConfigMap.KubeKind] = async (k, b, ns, ct) => await client.CreateNamespacedConfigMapAsync((V1ConfigMap)b, ns, cancellationToken: ct),
            [V1Secret.KubeKind] = async (k, b, ns, ct) => await client.CreateNamespacedSecretAsync((V1Secret)b, ns, cancellationToken: ct),
            [V1Deployment.KubeKind] = async (k, b, ns, ct) => await client.CreateNamespacedDeploymentAsync((V1Deployment)b, ns, cancellationToken: ct),
            [V1Service.KubeKind] = async (k, b, ns, ct) => await client.CreateNamespacedServiceAsync((V1Service)b, ns, cancellationToken: ct),
            [V1Pod.KubeKind] = async (k, n, ns, ct) => await client.CreateNamespacedPodAsync((V1Pod)n, ns, cancellationToken: ct)
        };

        /// <summary>
        /// DeleteMethods is a dictionary of methods, used to find the appropriate delete method based on the object Kind
        /// </summary>
        private static readonly Dictionary<string, DeleteLambda> DeleteMethods = new Dictionary<string, DeleteLambda>()
        {
            [V1ConfigMap.KubeKind] = async (k, n, ns, ct) => await client.DeleteNamespacedConfigMapAsync(n, ns, cancellationToken: ct),
            [V1Secret.KubeKind] = async (k, n, ns, ct) => await client.DeleteNamespacedSecretAsync(n, ns, cancellationToken: ct),
            [V1Deployment.KubeKind] = async (k, n, ns, ct) => await client.DeleteNamespacedDeploymentAsync(n, ns, cancellationToken: ct),
            [V1Service.KubeKind] = async (k, n, ns, ct) => await client.DeleteNamespacedServiceAsync(n, ns, cancellationToken: ct),
            [V1Pod.KubeKind] = async (k, n, ns, ct) => await client.DeleteNamespacedPodAsync(n, ns, cancellationToken: ct)
        };

        public static Kubernetes client;
        public static CancellationToken cancellationToken;

        // if the env var is not set, we use the default k8s namespace for KubeNamespace
        private static string KubeNamespace => !String.IsNullOrEmpty(Environment.GetEnvironmentVariable("NAMESPACE")) ? Environment.GetEnvironmentVariable("NAMESPACE") : "default";
        private async static Task Main(string[] args)
        {
            Console.WriteLine($"Program started... in namespace = {KubeNamespace}");
            
            // Initializes a new instance of the k8s.KubernetesClientConfiguration from default
            // locations If the KUBECONFIG environment variable is set, then that will be used.
            // Next, it looks for a config file at k8s.KubernetesClientConfiguration.KubeConfigDefaultLocation.
            // Then, it checks whether it is executing inside a cluster and will use k8s.KubernetesClientConfiguration.InClusterConfig.
            // Finally, if nothing else exists, it creates a default config with localhost:8080
            // as host.
            KubernetesClientConfiguration config = KubernetesClientConfiguration.BuildDefaultConfig();

            // cancellationToken used with async() calls
            CancellationTokenSource source = new CancellationTokenSource();
            cancellationToken = source.Token;

            // client is a k8s client used to issue the HTTP calls to K8s
            client = new Kubernetes(config);
            // objects a list used to store the objects
            List<IKubernetesObject> objects = new List<IKubernetesObject>();

            
            /// create the resources definitions:
            /// the order is important here. The secret and configmaps will be used
            /// when deploying the deployment
            
            // create secret
            IKubernetesObject mySecret = Factory.CreateSecretDefinition();
            objects.Add(mySecret);
            /// create configmaps
            IKubernetesObject myConfigMap = Factory.CreateConfigMapDefinition();
            objects.Add(myConfigMap);
            // create configmap to be mounted as volume
            IKubernetesObject myMountedConfigMap = Factory.CreateMountConfigMapDefinition();
            objects.Add(myMountedConfigMap);
            // create deployment
            IKubernetesObject myDeployment = Factory.CreateDeploymentDefinition();
            objects.Add(myDeployment);
            // create service
            IKubernetesObject myService = Factory.CreateServiceDefinition();
            objects.Add(myService);
            // create pod
            IKubernetesObject myPod = Factory.CreatePodDefinition();
            objects.Add(myPod);

            // delete all existing replicas of our objects
            await Cleanup(objects);

            // giving K8s some time to delete the entities
            Thread.Sleep(5000);

            // deploy all objects to K8s
            await DeployAll(objects);
            Thread.Sleep(2000);


            // Read the list of pods contained in default namespace
            //var list = client.CoreV1.ListNamespacedPod();

            //// Print the name of pods 
            //foreach (var item in list.Items)
            //{
            //    Console.WriteLine(item.Metadata.Name);
            //}
            //// Or empty if there are no pods
            //if (list.Items.Count == 0)
            //{
            //    Console.WriteLine("Empty!");
            //}
            //WatchUsingCallback(client);
            await PrintPodLabels(client);
        }

        /// <summary>
        /// Cleanup deletes all the objects
        /// </summary>
        /// <param name="objects"></param>
        /// <returns></returns>
        public async static Task Cleanup(List<IKubernetesObject> objects)
        {
            foreach (IKubernetesObject item in objects)
            {
                await DeleteEntities(item);
            }
        }

        /// <summary>
        /// DeployAll deploy all the objects
        /// </summary>
        /// <param name="objects"></param>
        /// <returns></returns>
        public async static Task DeployAll(List<IKubernetesObject> objects)
        {
            foreach (IKubernetesObject item in objects)
            {
                await DeployEntities(item);
            }
        }

        /// <summary>
        ///DeployEntities takes a K8s object -> fetches the corresponding create function -> deploys the object
        /// </summary>
        /// <param name="definition"></param>
        /// <returns></returns>
        public static async Task DeployEntities(IKubernetesObject definition)
        {
            try
            {
                CreateLambda create = CreateMethods[definition.Kind];
                await create(client, definition, KubeNamespace, cancellationToken);
                Console.WriteLine($"{definition.Kind} created");
            }
            catch (HttpOperationException e) when (e.Response.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                Console.Error.WriteLine($"{definition.Kind} already exists");
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"An error occured while trying to create resource: {e}");
            }
        }
        /// <summary>
        /// DeleteEntities takes a K8s object -> fetches the corresponding create function -> deletes the object
        /// </summary>
        /// <param name="definition"></param>
        /// <returns></returns>
        public async static Task DeleteEntities(IKubernetesObject definition)
        {  
            try
            {
                DeleteLambda delete = DeleteMethods[definition.Kind];
                await delete(client, GetName(definition), KubeNamespace, cancellationToken);
                Console.WriteLine($"{definition.Kind} {GetName(definition)} deleted");
            }
            catch (HttpOperationException e) when (e.Response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                Console.Error.WriteLine($"{definition.Kind} already deleted");
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"An error occured while trying to delete {definition.Kind} resource: {e}");
            }
        }

        /// <summary>
        /// GetName used to get a Name from a generic IKubernetesObject
        /// </summary>
        /// <param name="kubernetesObject"></param>
        /// <returns></returns>/
        public static string GetName(IKubernetesObject kubernetesObject)
        {
            return kubernetesObject.Kind switch
            {
                V1ConfigMap.KubeKind => ((V1ConfigMap)kubernetesObject).Metadata.Name,
                V1Secret.KubeKind => ((V1Secret)kubernetesObject).Metadata.Name,
                V1Deployment.KubeKind => ((V1Deployment)kubernetesObject).Metadata.Name,
                V1Service.KubeKind => ((V1Service)kubernetesObject).Metadata.Name,
                V1Pod.KubeKind => ((V1Pod)kubernetesObject).Metadata.Name,
                _ => throw new InvalidCastException($"Cannot cast {kubernetesObject.Kind} to a k8s class"),
            };
        }

        private static async Task NodesMetrics(IKubernetes client)
        {
            var nodesMetrics = await client.GetKubernetesNodesMetricsAsync().ConfigureAwait(false);

            foreach (var item in nodesMetrics.Items)
            {
                Console.WriteLine(item.Metadata.Name);

                foreach (var metric in item.Usage)
                {
                    Console.WriteLine($"{metric.Key}: {metric.Value}");
                }
            }
        }

        private static async Task PodsMetrics(IKubernetes client)
        {
            var podsMetrics = await client.GetKubernetesPodsMetricsAsync().ConfigureAwait(false);

            if (!podsMetrics.Items.Any())
            {
                Console.WriteLine("Empty");
            }

            foreach (var item in podsMetrics.Items)
            {
                foreach (var container in item.Containers)
                {
                    Console.WriteLine(container.Name);

                    foreach (var metric in container.Usage)
                    {
                        Console.WriteLine($"{metric.Key}: {metric.Value}");
                    }
                }

                Console.Write(Environment.NewLine);
            }
        }

        private static void WatchUsingCallback(IKubernetes client)
        {
            var podlistResp = client.CoreV1.ListNamespacedPodWithHttpMessagesAsync("default", watch: true);
            using (podlistResp.Watch<V1Pod, V1PodList>((type, item) =>
                   {
                       Console.WriteLine("==on watch event==");
                       Console.WriteLine(type);
                       Console.WriteLine(item.Metadata.Name);
                       Console.WriteLine("==on watch event==");
                   }))
            {
                Console.WriteLine("press ctrl + c to stop watching");

                var ctrlc = new ManualResetEventSlim(false);
                Console.CancelKeyPress += (sender, eventArgs) => ctrlc.Set();
                ctrlc.Wait();
            }
        }

        private static async Task PrintPodLogs(IKubernetes client)
        {
            var list = client.CoreV1.ListNamespacedPod("default");
            if (list.Items.Count == 0)
            {
                Console.WriteLine("No pods!");
                return;
            }

            var pod = list.Items[0];

            var response = await client.CoreV1.ReadNamespacedPodLogWithHttpMessagesAsync(
                pod.Metadata.Name,
                pod.Metadata.NamespaceProperty, container: pod.Spec.Containers[0].Name, follow: true).ConfigureAwait(false);
            var stream = response.Body;
            stream.CopyTo(Console.OpenStandardOutput());
        }

        private static async Task PrintPodLabels(IKubernetes client)
        {
            var list = client.CoreV1.ListNamespacedService("default");
            foreach (var item in list.Items)
            {
                Console.WriteLine("Pods for service: " + item.Metadata.Name);
                Console.WriteLine("=-=-=-=-=-=-=-=-=-=-=");
                if (item.Spec == null || item.Spec.Selector == null)
                {
                    continue;
                }

                var labels = new List<string>();
                foreach (var key in item.Spec.Selector)
                {
                    labels.Add(key.Key + "=" + key.Value);
                }

                var labelStr = string.Join(",", labels.ToArray());
                Console.WriteLine(labelStr);
                var podList = client.CoreV1.ListNamespacedPod("default", labelSelector: labelStr);
                foreach (var pod in podList.Items)
                {
                    Console.WriteLine(pod.Metadata.Name);
                }

                if (podList.Items.Count == 0)
                {
                    Console.WriteLine("Empty!");
                }

                Console.WriteLine();
            }
        }

        private static async Task PrintKubernetesMetrics(Kubernetes client)
        {
            var nodesMetrics = await client.GetKubernetesNodesMetricsAsync();
            foreach (var metric in nodesMetrics.Items)
            {
                Console.WriteLine("Node: " + metric.Metadata.Name);
                Console.WriteLine("CPU Usage: " + metric.Usage["cpu"]);
                Console.WriteLine("Memory Usage: " + metric.Usage["memory"]);
            }

            // Retrieve the metrics for all pods in the cluster
            var podsMetrics = await client.GetKubernetesPodsMetricsAsync();
            foreach (var metric in podsMetrics.Items)
            {
                Console.WriteLine("Pod: " + metric.Metadata.Name);
                Console.WriteLine("CPU Usage: " + metric.Containers[0].Usage["cpu"]);
                Console.WriteLine("Memory Usage: " + metric.Containers[0].Usage["memory"]);
            }

            // Retrieve the metrics for all deployment in the cluster
            var deploymentsMetrics = await client.ListDeploymentForAllNamespacesAsync();
            foreach (var deployment in deploymentsMetrics.Items)
            {
                Console.WriteLine("Deployment: " + deployment.Metadata.Name);
                Console.WriteLine("Replicas: " + deployment.Status.Replicas);
                Console.WriteLine("Available Replicas: " + deployment.Status.AvailableReplicas);
                Console.WriteLine("Unavailable Replicas: " + deployment.Status.UnavailableReplicas);
            }

            // Retrieve the metrics for all services in a specific namespace
            var servicesMetrics = await client.ListNamespacedServiceAsync("default");
            foreach (var service in servicesMetrics.Items)
            {
                Console.WriteLine("Service: " + service.Metadata.Name);
                Console.WriteLine("Namespace: " + service.Metadata.Namespace);
                Console.WriteLine("Cluster IP: " + service.Spec.ClusterIP);
                Console.WriteLine("External IPs: " + string.Join(",", service.Spec.ExternalIPs));
                Console.WriteLine("Ports: ");
                foreach (var port in service.Spec.Ports)
                {
                    Console.WriteLine("Name: " + port.Name);
                    Console.WriteLine("Port: " + port.Port);
                    Console.WriteLine("Protocol: " + port.Protocol);
                }
            }

        }

    }
}
