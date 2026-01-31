namespace Fwda.Watcher
{
    using System;
    using Docker.DotNet;

    public static class DockerClientFactory
    {
        public static IDockerClient CreateDockerClient()
        {
            var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST");
            if (!string.IsNullOrEmpty(dockerHost))
            {
                return CreateClientFromUri(dockerHost, "DOCKER_HOST");
            }

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                return CreateClientForUnix() ?? new DockerClientConfiguration().CreateClient();
            }

            if (OperatingSystem.IsWindows())
            {
                return CreateClientForWindows();
            }

            return CreateDefaultClient();
        }

        private static DockerClient CreateClientFromUri(string uri, string source)
        {
            Console.WriteLine($"[INFO] Connecting to Docker/Podman via {source}: {uri}");
            return new DockerClientConfiguration(new Uri(uri)).CreateClient();
        }

        private static DockerClient? CreateClientForUnix()
        {
            // Try Podman rootless socket first
            var podmanSocket = $"unix://{Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")}/podman/podman.sock";
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR")) && 
                File.Exists(podmanSocket.Replace("unix://", "")))
            {
                return CreateClientFromUri(podmanSocket, "Podman rootless socket");
            }

            // Try Docker socket
            var dockerSocket = "unix:///var/run/docker.sock";
            if (File.Exists("/var/run/docker.sock"))
            {
                return CreateClientFromUri(dockerSocket, "Docker socket");
            }
        
            // Try Podman system socket
            var podmanSystemSocket = "unix:///run/podman/podman.sock";
            if (File.Exists("/run/podman/podman.sock"))
            {
                return CreateClientFromUri(podmanSystemSocket, "Podman system socket");
            }

            return null;
        }

        private static DockerClient CreateClientForWindows()
        {
            // Windows named pipe for Docker Desktop or Podman
            var dockerPipe = "npipe://./pipe/docker_engine";
            return CreateClientFromUri(dockerPipe, "Windows named pipe");
        }

        private static DockerClient CreateDefaultClient()
        {
            Console.WriteLine("[INFO] Using default Docker connection");
            return new DockerClientConfiguration().CreateClient();
        }
    }
}
