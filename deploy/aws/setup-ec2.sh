#!/bin/bash
# EOD Burst System - EC2 Setup Script
# This runs automatically when the EC2 instance launches

set -e

echo "=========================================="
echo "  EOD Burst System - AWS Setup"
echo "=========================================="

# Update system
yum update -y

# Install Docker
yum install -y docker
systemctl start docker
systemctl enable docker

# Install Docker Compose
curl -L "https://github.com/docker/compose/releases/latest/download/docker-compose-$(uname -s)-$(uname -m)" -o /usr/local/bin/docker-compose
chmod +x /usr/local/bin/docker-compose
ln -s /usr/local/bin/docker-compose /usr/bin/docker-compose

# Install Git
yum install -y git

# Add ec2-user to docker group
usermod -aG docker ec2-user

# Clone the repository
cd /home/ec2-user
git clone https://github.com/tzaczek/eod-burst.git
cd eod-burst

# Setup environment
cp .env.example .env

# Pull and start all services
docker-compose pull
docker-compose up -d

# Create a status check script
cat > /home/ec2-user/check-status.sh << 'EOF'
#!/bin/bash
echo ""
echo "=========================================="
echo "  EOD Burst System Status"
echo "=========================================="
echo ""
docker-compose -f /home/ec2-user/eod-burst/docker-compose.yml ps
echo ""
echo "Public IP: $(curl -s http://169.254.169.254/latest/meta-data/public-ipv4)"
echo ""
echo "Access URLs:"
echo "  🧪 Test Dashboard: http://$(curl -s http://169.254.169.254/latest/meta-data/public-ipv4):8083"
echo "  📊 Grafana:        http://$(curl -s http://169.254.169.254/latest/meta-data/public-ipv4):3000"
echo "  🔍 Jaeger:         http://$(curl -s http://169.254.169.254/latest/meta-data/public-ipv4):16686"
echo ""
EOF
chmod +x /home/ec2-user/check-status.sh

# Set ownership
chown -R ec2-user:ec2-user /home/ec2-user/eod-burst
chown ec2-user:ec2-user /home/ec2-user/check-status.sh

echo ""
echo "=========================================="
echo "  Setup Complete!"
echo "=========================================="
echo ""
