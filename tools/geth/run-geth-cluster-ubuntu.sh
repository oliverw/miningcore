#!/bin/sh

cluster_size=$1
root_dir=$2

# Install jq if the system does not have it
which jq
RES=$?
if [ $RES -eq 1 ]; then
	sudo apt-get install -y jq
fi

get_address()
{
	# root_dir = $1
	# id = $2
	jq ".address" $1/$2/keystore/UTC* | tr -d '"'
}

get_network_id()
{
	# root_dir = $1
	jq '.config.chainId' $1/genesis.json
}

if [ -z "$cluster_size" ]; then
	cluster_size=1
fi

if [ -z "$root_dir" ]; then
	root_dir=/tmp/gethroot
fi

echo "Creating $cluster_size node(s) at $root_dir..."

# Create an account for each node
for i in $(seq 1 $cluster_size)
do
	id=$(printf "%02d" $i)
	mkdir -p $root_dir/$id/logs
	if [ ! -d "$root_dir/$id/keystore" ]; then
		echo $id > $root_dir/$id/password
		geth --datadir $root_dir/$id --password $root_dir/$id/password account new
		echo "Created account $(get_address $root_dir $id)"
	else
		echo "Account $(get_address $root_dir $id) already exists"
	fi
done

# Configure genesis.json
cp ./genesis.json $root_dir/genesis.json
sed -i "s/BALANCE_PLACEHOLDER/\"$(get_address $root_dir 01)\": { \"balance\": \"0x200000000000000000000000000000000000000000000000000000000000000\" }/g" $root_dir/genesis.json

# Init each node
for i in $(seq 1 $cluster_size)
do
	id=$(printf "%02d" $i)
	if [ ! -d "$root_dir/$id/geth" ]; then
		echo "Initializing node $id..."
		geth --datadir $root_dir/$id/ init $root_dir/genesis.json
	else
		echo "Node $id already initialized"
	fi
done

# Setup log dir
mkdir -p $root_dir/logs

script_dir=$(pwd)
cd $root_dir

# Init boot node
if [ ! -f $root_dir/boot.key ]; then
	echo "Initializing boot node..."
	bootnode -genkey boot.key
else
	echo "Boot node already initialized"
fi

timestamp=$(date +%s)

# Start boot node
ps -ef | grep bootnode | grep -v "grep"
RES=$?
if [ $RES -eq 1 ]; then
	echo "Starting boot node..."
	boot_log=$root_dir/logs/boot-$timestamp.log
	nohup bootnode -nodekey boot.key -verbosity 2 -addr :30310 > $boot_log 2>&1 &
else
	boot_log=$(ls -Art $root_dir/logs/boot* | tail -n 1)
	echo "Boot node already running and logging at $boot_log"
fi

# Wait for boot node to come up
sleep 1

# Get enode
enode=$(grep enode $boot_log)

# Run nodes
for i in $(seq 1 $cluster_size)
do
	id=$(printf "%02d" $i)
	ps -ef | grep geth | grep -v "grep" | grep "port 313$id"
	RES=$?
	if [ $RES -eq 1 ]; then
		echo "Starting node $id..."
		nohup geth --datadir $root_dir/$id --syncmode 'full' --port 313$id --http --http.addr 'localhost' --http.port 85$id --http.api 'admin,debug,web3,eth,txpool,personal,ethash,miner,net' --ws --ws.addr 'localhost' --ws.port 86$id --ws.api 'admin,debug,web3,eth,txpool,personal,ethash,miner,net' --bootnodes "$enode" --networkid $(get_network_id $root_dir) --miner.gasprice '1' --allow-insecure-unlock -unlock "0x$(get_address $root_dir $id)" --password $root_dir/$id/password --mine > $root_dir/$id/logs/node$id-$timestamp.log 2>&1 &
	else
		echo "Node $id already running"
	fi
done
