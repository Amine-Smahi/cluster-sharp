#!/bin/bash

# Setup machine script - installs Docker and necessary dependencies
# Generated from setup-machine-commands.json

set -e  # Exit on error

echo "Starting machine setup..."

# Update system packages
sudo apt-get update -y
sudo apt-get upgrade -y

# Install prerequisites
sudo apt-get install ca-certificates curl -y

# Setup Docker repository
sudo install -m 0755 -d /etc/apt/keyrings
sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
sudo chmod a+r /etc/apt/keyrings/docker.asc
echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu $(. /etc/os-release && echo "${UBUNTU_CODENAME:-$VERSION_CODENAME}") stable" | sudo tee /etc/apt/sources.list.d/docker.list > /dev/null

# Install Docker
sudo apt-get update -y
sudo apt-get install docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin -y

# Add current user to docker group
sudo usermod -aG docker $USER
echo "Added current user to docker group. You may need to log out and back in for this to take effect."

# Clean up
sudo apt-get autoremove -y
sudo apt-get clean

# Test Docker installation
echo "Testing Docker installation..."
sudo docker run hello-world

echo "Machine setup complete!" 