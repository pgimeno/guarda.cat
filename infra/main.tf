provider "aws" {
  region = var.region

  default_tags {
    tags = {
      Project     = var.project
      Environment = var.environment
      Owner       = var.owner
      ManagedBy   = "Terraform"
    }
  }
}

data "aws_availability_zones" "available" {
  state = "available"
}

data "aws_caller_identity" "current" {}

locals {
  name            = "${var.project}-${var.environment}"
  azs             = slice(data.aws_availability_zones.available.names, 0, 2)
  application_url = "https://${var.app_domain}"
  container_image = "${aws_ecr_repository.application.repository_url}:${var.image_tag}"
}
