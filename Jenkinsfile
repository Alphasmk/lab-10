pipeline {
    agent any
    options {
        skipDefaultCheckout()
        disableConcurrentBuilds()
    }
    stages {
        stage('Checkout') {
            steps {
                checkout scm
                bat 'git rev-parse HEAD'
            }
        }
        stage('Restore') {
            steps { bat 'dotnet restore EventManagement.slnx' }
        }
        stage('Build') {
            steps { bat 'dotnet build EventManagement.slnx --configuration Release --no-restore' }
        }
        stage('Test') {
            steps { powershell '.\\ci\\test.ps1' }
            post {
                always { archiveArtifacts artifacts: 'TestResults/**', allowEmptyArchive: true }
            }
        }
        stage('Package') {
            steps {
                bat 'docker build -f Server/Dockerfile -t riot-server:%BUILD_NUMBER% .'
                bat 'docker image inspect riot-server:%BUILD_NUMBER%'
            }
        }
        stage('Deploy') {
            steps {
                bat 'kubectl set image deployment/riot-server server=riot-server:%BUILD_NUMBER%'
                bat 'kubectl rollout status deployment/riot-server --timeout=180s'
                bat 'kubectl get deployment riot-server -o wide'
                bat 'kubectl get pods -l app=riot-server -o wide'
            }
        }
    }
}
