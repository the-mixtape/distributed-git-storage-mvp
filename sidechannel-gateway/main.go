package main

import (
	"fmt"
	"io"
	"log"
	"net/http"

	"github.com/sirupsen/logrus"
	"gitlab.com/gitlab-org/gitaly/v16/client"
	"gitlab.com/gitlab-org/gitaly/v16/proto/go/gitalypb"
)

func main() {
	mux := http.NewServeMux()
	mux.HandleFunc("/health", func(w http.ResponseWriter, _ *http.Request) {
		w.WriteHeader(http.StatusOK)
	})
	mux.HandleFunc("/upload-pack", uploadPack)

	log.Println("sidechannel gateway listening on :8090")
	log.Fatal(http.ListenAndServe(":8090", mux))
}

func uploadPack(w http.ResponseWriter, r *http.Request) {
	address := r.URL.Query().Get("address")
	storage := r.URL.Query().Get("storage")
	path := r.URL.Query().Get("path")
	protocol := r.URL.Query().Get("protocol")
	if address == "" || storage == "" || path == "" {
		http.Error(w, "address, storage and path are required", http.StatusBadRequest)
		return
	}

	logger := logrus.New()
	logger.SetOutput(io.Discard)
	registry := client.NewSidechannelRegistry(logger)
	connection, err := client.DialSidechannel(r.Context(), address, registry, nil)
	if err != nil {
		http.Error(w, fmt.Sprintf("dial Gitaly: %v", err), http.StatusBadGateway)
		return
	}
	defer connection.Close()

	ctx, waiter := registry.Register(r.Context(), func(sidechannel client.SidechannelConn) error {
		if _, err := io.Copy(sidechannel, r.Body); err != nil {
			return fmt.Errorf("copy request to Gitaly: %w", err)
		}
		if err := sidechannel.CloseWrite(); err != nil {
			return fmt.Errorf("close Gitaly request stream: %w", err)
		}

		w.Header().Set("Content-Type", "application/x-git-upload-pack-result")
		w.WriteHeader(http.StatusOK)
		if _, err := io.Copy(w, sidechannel); err != nil {
			return fmt.Errorf("copy Gitaly response: %w", err)
		}
		return nil
	})

	_, rpcErr := gitalypb.NewSmartHTTPServiceClient(connection).PostUploadPackWithSidechannel(
		ctx,
		&gitalypb.PostUploadPackWithSidechannelRequest{
			Repository: &gitalypb.Repository{
				StorageName:  storage,
				RelativePath: path,
			},
			GitProtocol: protocol,
		},
	)
	waitErr := waiter.Close()
	if rpcErr != nil || waitErr != nil {
		log.Printf("upload-pack failed: rpc=%v sidechannel=%v", rpcErr, waitErr)
		if rpcErr != nil {
			http.Error(w, fmt.Sprintf("Gitaly RPC: %v", rpcErr), http.StatusBadGateway)
		}
	}
}
