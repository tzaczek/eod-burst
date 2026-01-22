# AWS Deployment Guide - EOD Burst System

Deploy the complete EOD Burst System on a single AWS EC2 instance for ~$0.17/hour.

## 📋 Prerequisites

1. AWS Account with credits
2. AWS CLI installed and configured (`aws configure`)
3. SSH key pair in AWS (or create one)

## 🚀 Quick Deploy (5 minutes)

### Step 1: Launch EC2 Instance

Run this in your terminal (replace `YOUR_KEY_NAME` with your SSH key):

```bash
# Set your SSH key name
KEY_NAME="your-key-name"

# Launch t3.xlarge instance with Amazon Linux 2023
aws ec2 run-instances \
  --image-id ami-0c7217cdde317cfec \
  --instance-type t3.xlarge \
  --key-name $KEY_NAME \
  --security-group-ids $(aws ec2 create-security-group \
    --group-name eod-burst-sg \
    --description "EOD Burst System" \
    --query 'GroupId' --output text) \
  --user-data file://setup-ec2.sh \
  --tag-specifications 'ResourceType=instance,Tags=[{Key=Name,Value=eod-burst-system}]' \
  --block-device-mappings '[{"DeviceName":"/dev/xvda","Ebs":{"VolumeSize":50,"VolumeType":"gp3"}}]'
```

### Step 2: Open Required Ports

```bash
# Get your security group ID
SG_ID=$(aws ec2 describe-security-groups --group-names eod-burst-sg --query 'SecurityGroups[0].GroupId' --output text)

# Open ports
aws ec2 authorize-security-group-ingress --group-id $SG_ID --protocol tcp --port 22 --cidr 0.0.0.0/0
aws ec2 authorize-security-group-ingress --group-id $SG_ID --protocol tcp --port 8083 --cidr 0.0.0.0/0
aws ec2 authorize-security-group-ingress --group-id $SG_ID --protocol tcp --port 3000 --cidr 0.0.0.0/0
aws ec2 authorize-security-group-ingress --group-id $SG_ID --protocol tcp --port 16686 --cidr 0.0.0.0/0
aws ec2 authorize-security-group-ingress --group-id $SG_ID --protocol tcp --port 8090 --cidr 0.0.0.0/0
aws ec2 authorize-security-group-ingress --group-id $SG_ID --protocol tcp --port 9001 --cidr 0.0.0.0/0
```

### Step 3: Get Public IP

```bash
# Wait ~2 minutes, then get the public IP
aws ec2 describe-instances \
  --filters "Name=tag:Name,Values=eod-burst-system" "Name=instance-state-name,Values=running" \
  --query 'Reservations[0].Instances[0].PublicIpAddress' \
  --output text
```

### Step 4: Access Your System

Wait ~5 minutes for setup to complete, then open:

| Service | URL |
|:--------|:----|
| 🧪 **Test Dashboard** | `http://<PUBLIC_IP>:8083` |
| 📊 Grafana | `http://<PUBLIC_IP>:3000` |
| 🔍 Jaeger | `http://<PUBLIC_IP>:16686` |
| 📨 Kafka UI | `http://<PUBLIC_IP>:8090` |
| 📦 MinIO | `http://<PUBLIC_IP>:9001` |

## 🛑 Stop to Save Money

```bash
# Stop instance (keeps data, stops billing for compute)
INSTANCE_ID=$(aws ec2 describe-instances --filters "Name=tag:Name,Values=eod-burst-system" --query 'Reservations[0].Instances[0].InstanceId' --output text)
aws ec2 stop-instances --instance-ids $INSTANCE_ID

# Start again when needed
aws ec2 start-instances --instance-ids $INSTANCE_ID
```

## 🗑️ Complete Cleanup

```bash
# Terminate instance
aws ec2 terminate-instances --instance-ids $INSTANCE_ID

# Delete security group (after instance terminates)
aws ec2 delete-security-group --group-name eod-burst-sg
```

## 💡 Tips

- **Stop when not using** - You're only charged when running
- **Use Spot Instances** - 70% cheaper (but can be interrupted)
- **Set billing alerts** - AWS Console → Billing → Budgets
