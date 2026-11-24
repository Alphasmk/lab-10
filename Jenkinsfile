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
                sh 'git rev-parse HEAD'
            }
        }
        stage('Restore') {
            steps { sh 'dotnet restore EventManagement.slnx' }
        }
        stage('Build') {
            steps { sh 'dotnet build EventManagement.slnx --configuration Release --no-restore' }
        }
        stage('Test') {
            steps { sh 'bash ci/test.sh' }
            post {
                always { archiveArtifacts artifacts: 'TestResults/**', allowEmptyArchive: true }
            }
        }
        stage('Package') {
            steps {
                sh 'docker build -f Server/Dockerfile -t riot-server:$BUILD_NUMBER .'
                sh 'docker image inspect riot-server:$BUILD_NUMBER'
            }
        }
        stage('Deploy') {
            steps {
                sh 'kubectl set image deployment/riot-server server=riot-server:$BUILD_NUMBER'
                sh 'kubectl rollout status deployment/riot-server --timeout=180s'
                sh 'kubectl get deployment riot-server -o wide'
                sh 'kubectl get pods -l app=riot-server -o wide'
            }
        }
    }
}
